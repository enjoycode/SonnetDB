use sonnetdb::Connection;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let data_dir = std::env::temp_dir().join(format!(
        "sonnetdb-rust-quickstart-{}",
        std::process::id()
    ));

    println!("SonnetDB native version: {}", sonnetdb::version()?);

    let connection = Connection::open_path(&data_dir)?;
    connection.execute_non_query("CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)")?;

    let inserted = connection.execute_non_query(
        "INSERT INTO cpu (time, host, usage) VALUES \
         (1710000000000, 'edge-1', 0.42),\
         (1710000001000, 'edge-1', 0.73)",
    )?;
    println!("inserted rows: {inserted}");

    let mut result =
        connection.execute("SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10")?;
    println!("{}", result.columns()?.join("\t"));

    while result.next()? {
        let timestamp = result.get_i64(0)?;
        let host = result.get_text(1)?.unwrap_or_default();
        let usage = result.get_f64(2)?;
        println!("{timestamp}\t{host}\t{usage:.3}");
    }

    println!("data directory: {}", data_dir.display());
    Ok(())
}
