fn main() {
    println!("cargo:rerun-if-changed=../../../shared/slopworld.proto");
    prost_build::Config::new()
        .compile_protos(&["../../../shared/slopworld.proto"], &["../../../shared"])
        .unwrap();
}
