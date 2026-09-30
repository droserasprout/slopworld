use super::*;

#[test]
fn an_empty_user_directory_has_no_stations() {
    let dir = temp_dir("empty");
    let catalog = Catalog::load_from(&dir);
    assert!(catalog.stations.is_empty());
    std::fs::remove_dir_all(dir).unwrap();
}

#[test]
fn user_presets_write_edit_and_delete_as_toml() {
    let dir = temp_dir("crud");
    let station = Station {
        id: "custom".into(),
        default_rate: 96,
        metadata: Metadata {
            name: "Custom Radio".into(),
            donate: "https://example.org/donate".into(),
            title_regex: String::new(),
        },
        streams: vec![
            Stream {
                rate: 96,
                key: "custom96".into(),
                url: "https://example.org/stream".into(),
            },
            Stream {
                rate: 64,
                key: "custom64".into(),
                url: "https://example.org/stream-64".into(),
            },
        ],
    };
    save_user_in(&dir, station).unwrap();

    let loaded = Catalog::load_from(&dir);
    assert_eq!(loaded.stations[0].metadata.name, "Custom Radio");
    assert_eq!(loaded.stations[0].streams.len(), 2);
    assert_eq!(
        loaded.stations[0].streams[0].url,
        "https://example.org/stream"
    );
    assert_eq!(
        loaded.stations[0].streams[1].url,
        "https://example.org/stream-64"
    );

    let mut edited = loaded.stations[0].clone();
    edited.metadata.name = "Edited Radio".into();
    save_user_in(&dir, edited).unwrap();
    assert_eq!(
        Catalog::load_from(&dir).stations[0].metadata.name,
        "Edited Radio"
    );
    assert_eq!(Catalog::load_from(&dir).stations[0].streams.len(), 2);

    delete_user_in(&dir, "custom").unwrap();
    assert!(Catalog::load_from(&dir).stations.is_empty());
    std::fs::remove_dir_all(dir).unwrap();
}

#[test]
fn user_shape_parses_and_resolves_without_serializing_urls() {
    let station = parse(
        r#"
id = "custom"
default_rate = 96

[metadata]
name = "Custom Radio"
donate = "https://example.org/donate"
title_regex = '^(?<title>.+?) by (?<artist>.+?)$'

[[stream]]
rate = 96
key = "custom96"
url = "https://example.org/stream"
"#,
        Path::new("custom.toml"),
    )
    .unwrap();
    assert_eq!(station.metadata.name, "Custom Radio");
    assert_eq!(station.metadata.donate, "https://example.org/donate");
    assert_eq!(
        station.metadata.title_regex,
        "^(?<title>.+?) by (?<artist>.+?)$"
    );
    assert_eq!(station.streams[0].key, "custom96");
    assert_eq!(station.streams[0].url, "https://example.org/stream");
    let json = serde_json::to_string(&station).unwrap();
    assert!(!json.contains("example.org/stream"));
    assert!(json.contains("custom96"));
    assert!(json.contains("title_regex"));
}

#[test]
fn resolves_a_local_fixture_without_fetching_it() {
    let station = parse(
        r#"
id = "fixture"

[[stream]]
rate = 1
key = "fixture"
url = "http://127.0.0.1:9/not-a-server"
"#,
        Path::new("fixture.toml"),
    )
    .unwrap();
    let mut catalog = Catalog::default();
    catalog.merge(station);
    assert_eq!(
        catalog.resolve("fixture", "fixture").unwrap(),
        "http://127.0.0.1:9/not-a-server"
    );
}

#[test]
fn a_user_catalog_reaches_the_mod_as_named_streams() {
    let dir = temp_dir("wire");
    std::fs::write(
        dir.join("fixture.toml"),
        r#"
id = "fixture"

[[stream]]
rate = 96
key = "fixture96"
url = "http://127.0.0.1:9/fixture"
"#,
    )
    .unwrap();
    let catalog = Catalog::load_from(&dir);

    let json: serde_json::Value = serde_json::to_value(&catalog).unwrap();
    for station in json["stations"].as_array().unwrap() {
        let streams = station["streams"].as_array().unwrap();
        assert!(!streams.is_empty());
        assert!(station["stream"].is_null());
        for stream in streams {
            assert!(stream["rate"].as_u64().unwrap() > 0);
            assert!(!stream["key"].as_str().unwrap().is_empty());
            assert!(stream["url"].is_null());
        }
    }
    std::fs::remove_dir_all(dir).unwrap();
}

#[test]
fn invalid_station_is_rejected() {
    let error = parse(
        "id = 'bad'\n[[stream]]\nrate = 0\nurl = 'http://example.org'",
        Path::new("bad.toml"),
    )
    .unwrap_err()
    .to_string();
    assert!(error.contains("positive"));
}

#[test]
fn personal_files_are_loaded_and_can_omit_the_id() {
    let dir = temp_dir("personal");
    std::fs::write(
        dir.join("personal.TOML"),
        r#"
[metadata]
name = "Personal Radio"

[[stream]]
rate = 96
url = "https://example.org/personal"
"#,
    )
    .unwrap();

    let catalog = Catalog::load_from(&dir);
    assert_eq!(catalog.stations.len(), 1);
    let personal = &catalog.stations[0];
    assert_eq!(personal.id, "personal");
    assert_eq!(personal.metadata.name, "Personal Radio");
    assert_eq!(personal.streams[0].key, "96");

    std::fs::remove_dir_all(dir).unwrap();
}

#[test]
fn startup_keeps_valid_stations_but_reload_rejects_partial_catalogs() {
    let dir = temp_dir("partial");
    std::fs::write(dir.join("a-broken.toml"), "[invalid").unwrap();
    std::fs::write(
            dir.join("b-valid.TOML"),
            "[metadata]\nname = \"Valid\"\n[[stream]]\nrate = 96\nurl = \"https://example.org/radio\"\n",
        )
        .unwrap();
    let startup = Catalog::load_from(&dir);
    assert_eq!(startup.stations.len(), 1);
    assert_eq!(startup.stations[0].metadata.name, "Valid");
    Catalog::try_load_from(&dir).unwrap_err();
    std::fs::remove_file(dir.join("a-broken.toml")).unwrap();
    assert_eq!(Catalog::try_load_from(&dir).unwrap().stations.len(), 1);
    std::fs::remove_dir_all(&dir).unwrap();
    assert!(Catalog::try_load_from(&dir).unwrap().stations.is_empty());
}

fn temp_dir(label: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!(
        "slopworld-jukebox-{label}-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    std::fs::create_dir_all(&dir).unwrap();
    dir
}
