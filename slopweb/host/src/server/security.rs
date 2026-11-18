//! Browser provenance and explicit host admission; forwarded headers confer no trust.
use anyhow::{Result, bail};
use axum::http::HeaderMap;
use url::Url;

pub(super) struct Boundary {
    local_hosts: [String; 2],
    public_origin: Option<String>,
    public_host: Option<String>,
}

impl Boundary {
    pub fn new(port: u16, public_origin: Option<String>) -> Result<Self> {
        let public_host = if let Some(origin) = &public_origin {
            let parsed = Url::parse(origin)?;
            if parsed.scheme() != "https" || parsed.origin().ascii_serialization() != *origin {
                bail!("Public origin must be an exact HTTPS origin without a path or credentials");
            }
            Some(parsed[url::Position::BeforeHost..url::Position::AfterPort].to_owned())
        } else {
            None
        };
        Ok(Self {
            local_hosts: [format!("127.0.0.1:{port}"), format!("localhost:{port}")],
            public_origin,
            public_host,
        })
    }

    pub fn admits_host(&self, headers: &HeaderMap) -> bool {
        single(headers, "host").is_some_and(|host| {
            self.local_hosts.iter().any(|local| local == host)
                || self.public_host.as_deref() == Some(host)
        })
    }

    pub fn origin(&self, headers: &HeaderMap) -> String {
        self.public_origin
            .clone()
            .unwrap_or_else(|| format!("http://{}", single(headers, "host").unwrap_or_default()))
    }

    pub fn same_origin(&self, headers: &HeaderMap) -> bool {
        let expected = self.origin(headers);
        let site = single(headers, "sec-fetch-site");
        if headers.contains_key("sec-fetch-site") && site != Some("same-origin") {
            return false;
        }
        if headers.contains_key("origin") {
            return single(headers, "origin") == Some(expected.as_str());
        }
        if headers.contains_key("referer") {
            return single(headers, "referer")
                .and_then(|value| Url::parse(value).ok())
                .is_some_and(|value| value.origin().ascii_serialization() == expected);
        }
        site == Some("same-origin")
    }
}

pub(super) fn single<'a>(headers: &'a HeaderMap, name: &str) -> Option<&'a str> {
    let mut values = headers.get_all(name).iter();
    let value = values.next()?.to_str().ok()?;
    if values.next().is_some() {
        return None;
    }
    Some(value)
}
