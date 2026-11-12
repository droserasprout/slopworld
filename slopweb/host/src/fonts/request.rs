//! Validate the backward-compatible selection and fallback wire requests.
use anyhow::{Context, Result, ensure};
use serde_json::Value;

pub(super) struct Selection {
    pub ui: String,
    pub terminal: String,
    pub ui_size: i64,
    pub terminal_size: i64,
}
pub(super) enum Request {
    Selection(Selection),
    Fallback { family: String, scalars: Vec<u32> },
}
pub(super) fn validate_family(name: &str) -> Result<()> {
    ensure!(
        name.chars().count() <= 256 && !name.chars().any(|c| c < ' '),
        "Invalid font family"
    );
    Ok(())
}
fn family(value: &Value, key: &str, default: &str) -> Result<String> {
    let name = value
        .get(key)
        .map_or(Ok(default), |v| v.as_str().context("Invalid font family"))?;
    validate_family(name)?;
    Ok(name.to_owned())
}
fn size(value: &Value, key: &str, low: i64, high: i64) -> Result<i64> {
    let size = value
        .get(key)
        .map_or(Ok(14), |v| v.as_i64().context("Invalid font size"))?;
    ensure!((low..=high).contains(&size), "Invalid font size");
    Ok(size)
}
pub(super) fn parse_request(value: &Value) -> Result<Request> {
    ensure!(value.is_object(), "Invalid font request");
    ensure!(
        value.get("face").is_none(),
        "Host glyph rasterization is not supported"
    );
    if let Some(fallback) = value.get("fallback") {
        ensure!(fallback.is_object(), "Invalid fallback request");
        let name = fallback
            .get("family")
            .and_then(Value::as_str)
            .context("Invalid fallback family")?;
        validate_family(name)?;
        let values = fallback["scalars"]
            .as_array()
            .context("Invalid fallback scalars")?;
        ensure!(
            (1..=256).contains(&values.len()),
            "Invalid fallback scalars"
        );
        let mut scalars = Vec::new();
        for v in values {
            let scalar = v.as_u64().context("Invalid Unicode scalar")?;
            ensure!(
                scalar <= 0x10ffff && !(0xd800..=0xdfff).contains(&scalar),
                "Invalid Unicode scalar"
            );
            scalars.push(scalar as u32);
        }
        scalars.sort_unstable();
        scalars.dedup();
        return Ok(Request::Fallback {
            family: name.to_owned(),
            scalars,
        });
    }
    let density = value
        .get("density")
        .map_or(Some(1.), Value::as_f64)
        .context("Invalid font density")?;
    ensure!((0.5..=4.).contains(&density), "Invalid font density");
    let ui_size = size(value, "uiFontSize", 0, 48)?;
    Ok(Request::Selection(Selection {
        ui: family(value, "uiFontName", "")?,
        terminal: family(value, "fontName", "monospace")?,
        ui_size: if ui_size == 0 { 13 } else { ui_size },
        terminal_size: size(value, "fontSize", 8, 28)?,
    }))
}
