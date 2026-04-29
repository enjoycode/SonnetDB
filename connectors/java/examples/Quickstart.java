package com.sonnetdb.examples;

import com.sonnetdb.SonnetDbConnection;
import com.sonnetdb.SonnetDbResult;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * SonnetDB Java connector quickstart.
 */
public final class Quickstart {
    private Quickstart() {
    }

    public static void main(String[] args) throws IOException {
        Path dataDir = Files.createTempDirectory("sonnetdb-java-quickstart-");
        run(dataDir);
        System.out.println("data directory: " + dataDir);
    }

    private static void run(Path dataDir) {
        System.out.println("SonnetDB native version: " + SonnetDbConnection.version());

        try (SonnetDbConnection connection = SonnetDbConnection.open(dataDir.toString())) {
            connection.executeNonQuery("CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)");
            int inserted = connection.executeNonQuery(
                "INSERT INTO cpu (time, host, usage) VALUES " +
                    "(1710000000000, 'edge-1', 0.42)," +
                    "(1710000001000, 'edge-1', 0.73)");
            System.out.println("inserted rows: " + inserted);

            try (SonnetDbResult result = connection.execute(
                "SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10")) {
                for (int i = 0; i < result.columnCount(); i++) {
                    if (i > 0) {
                        System.out.print("\t");
                    }
                    System.out.print(result.columnName(i));
                }
                System.out.println();

                while (result.next()) {
                    System.out.printf(
                        "%d\t%s\t%.3f%n",
                        result.getLong(0),
                        result.getString(1),
                        result.getDouble(2));
                }
            }
        }
    }

}
