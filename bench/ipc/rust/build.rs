fn main() {
    println!("cargo:rerun-if-changed=../../../shared/slopworld.proto");
    prost_build::Config::new()
        .type_attribute(".", "#[derive(serde::Serialize, serde::Deserialize)]")
        .compile_protos(&["../../../shared/slopworld.proto"], &["../../../shared"]).unwrap();
}
