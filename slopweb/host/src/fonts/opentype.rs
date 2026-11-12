//! OpenType metadata and standalone extraction mirror NativeFonts' regular-face policy.
//! The persistent catalog owns discovery and storage; this module only reads bytes.
use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Serialize, Deserialize)]
pub(super) struct Face {
    pub family: String,
    pub mono: bool,
    pub score: i32,
    pub hash: String,
    pub byte_len: usize,
    pub coverage: Vec<[u32; 2]>,
}

fn u16_at(bytes: &[u8], offset: usize) -> Result<u16> {
    Ok(u16::from_be_bytes(
        bytes
            .get(offset..offset + 2)
            .context("Truncated font")?
            .try_into()?,
    ))
}
fn u32_at(bytes: &[u8], offset: usize) -> Result<u32> {
    Ok(u32::from_be_bytes(
        bytes
            .get(offset..offset + 4)
            .context("Truncated font")?
            .try_into()?,
    ))
}
fn put(bytes: &mut [u8], offset: usize, value: u32) {
    bytes[offset..offset + 4].copy_from_slice(&value.to_be_bytes());
}

pub(super) fn extract(bytes: &[u8], index: u32) -> Result<Vec<u8>> {
    if bytes.get(..4) != Some(b"ttcf") {
        ensure!(index == 0, "Invalid standalone face index");
        return Ok(bytes.to_vec());
    }
    ensure!(index < u32_at(bytes, 8)?, "Invalid collection face index");
    let header = u32_at(bytes, 12 + index as usize * 4)? as usize;
    let count = u16_at(bytes, header + 4)? as usize;
    let directory = 12 + count * 16;
    let mut result = bytes
        .get(header..header + directory)
        .context("Truncated font directory")?
        .to_vec();
    let mut adjustment = None;
    for i in 0..count {
        let record = 12 + i * 16;
        let from = u32_at(&result, record + 8)? as usize;
        let length = u32_at(&result, record + 12)? as usize;
        let table = bytes
            .get(from..from.checked_add(length).context("Font overflow")?)
            .context("Truncated font table")?;
        let offset = result.len();
        put(&mut result, record + 8, offset.try_into()?);
        result.extend_from_slice(table);
        result.resize((result.len() + 3) & !3, 0);
        if &result[record..record + 4] == b"head" && length >= 12 {
            adjustment = Some(offset + 8);
            put(&mut result, offset + 8, 0);
        }
    }
    if let Some(offset) = adjustment {
        let checksum = result.as_chunks::<4>().0.iter().fold(0u32, |sum, word| {
            sum.wrapping_add(u32::from_be_bytes(*word))
        });
        put(&mut result, offset, 0xb1b0afbau32.wrapping_sub(checksum));
    }
    Ok(result)
}

pub(super) fn metadata(bytes: &[u8], index: u32, coverage: bool) -> Result<Face> {
    let parsed = ttf_parser::Face::parse(bytes, index).context("Unsupported font face")?;
    let mut best = None;
    let mut rank = -1;
    for name in parsed.names() {
        if !matches!(name.name_id, 1 | 16)
            || !matches!(
                name.platform_id,
                ttf_parser::PlatformId::Unicode | ttf_parser::PlatformId::Windows
            )
        {
            continue;
        }
        let candidate = i32::from(name.name_id == 16) * 2 + i32::from(name.language_id == 0x409);
        if candidate <= rank {
            continue;
        }
        // NativeFonts accepts every Windows encoding as UTF-16BE here.
        let utf16: Vec<u16> = name
            .name
            .as_chunks::<2>()
            .0
            .iter()
            .map(|v| u16::from_be_bytes([v[0], v[1]]))
            .collect();
        let text = String::from_utf16_lossy(&utf16)
            .trim_matches(['\0', ' '])
            .to_owned();
        if !text.is_empty() {
            best = Some(text);
            rank = candidate;
        }
    }
    let raw = parsed.raw_face();
    let score = raw
        .table(ttf_parser::Tag::from_bytes(b"OS/2"))
        .filter(|t| t.len() >= 64)
        .and_then(|t| {
            let weight = u16_at(t, 4).ok()? as i32;
            let width = u16_at(t, 6).ok()? as i32;
            ((1..=1000).contains(&weight) && (1..=9).contains(&width))
                .then(|| distance(weight, width, u16_at(t, 62).unwrap() & 0x201 != 0))
        })
        .or_else(|| {
            raw.table(ttf_parser::Tag::from_bytes(b"head"))
                .filter(|t| t.len() >= 46)
                .map(|t| {
                    let style = u16_at(t, 44).unwrap();
                    distance(if style & 1 != 0 { 700 } else { 400 }, 5, style & 2 != 0)
                })
        })
        .unwrap_or(i32::MAX);
    let mono = raw
        .table(ttf_parser::Tag::from_bytes(b"post"))
        .and_then(|t| u32_at(t, 12).ok())
        .is_some_and(|v| v != 0);
    let mut scalars = Vec::new();
    if let Some(cmap) = parsed.tables().cmap.filter(|_| coverage) {
        for table in cmap.subtables {
            if !table.is_unicode() {
                continue;
            }
            table.codepoints(|c| {
                if char::from_u32(c).is_some() && table.glyph_index(c).is_some_and(|g| g.0 != 0) {
                    scalars.push(c);
                }
            });
        }
    }
    scalars.sort_unstable();
    scalars.dedup();
    Ok(Face {
        family: best.context("Font has no Unicode family name")?,
        mono,
        score,
        hash: String::new(),
        byte_len: bytes.len(),
        coverage: compress(&scalars),
    })
}
fn distance(weight: i32, width: i32, italic: bool) -> i32 {
    i32::from(italic) * 100_000 + (width - 5).abs() * 10_000 + (weight - 400).abs()
}
pub(super) fn compress(scalars: &[u32]) -> Vec<[u32; 2]> {
    let mut result: Vec<[u32; 2]> = Vec::new();
    for &scalar in scalars {
        if let Some(last) = result.last_mut()
            && last[1] + 1 == scalar
        {
            last[1] = scalar;
            continue;
        }
        result.push([scalar, scalar]);
    }
    result
}
pub(super) fn contains(ranges: &[[u32; 2]], scalar: u32) -> bool {
    let index = ranges.partition_point(|range| range[1] < scalar);
    ranges.get(index).is_some_and(|range| range[0] <= scalar)
}
