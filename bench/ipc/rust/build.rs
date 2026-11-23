fn main() -> std::io::Result<()> {
    println!("cargo:rerun-if-changed=../../../shared/slopworld.proto");
    prost_build::Config::new()
        .compile_protos(&["../../../shared/slopworld.proto"], &["../../../shared"])
}
