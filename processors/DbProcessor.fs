module DbProcessor

open Microsoft.Data.SqlClient
open Util
open DbProcessorMessage
open Models

/// ======================================================================
/// Database processor
/// ----------------------------------------------------------------------
/// - Receives word records from the validation processor.
/// - Updates the database with the word records.
/// ======================================================================

/// Configuration options for the database processor.
type Configuration =
    { /// Number of words to process in a batch.
      batchSize: int 

      /// Connection string for the database.
      connectionString: string }

/// Filter out duplicate words from the list.
let distinct words = words |> Array.distinctBy (fun w -> w.word)    

/// Update the database with the given word records.
let updateWords (connection : SqlConnection) (words : WordRecord array) =
    if words.Length = 0 then
        printfn "No words to update."    
        0
    else 
        use command = connection.CreateCommand()    
    
        // Add parameters for each word record, building a list of the parameters
        // added suitable for inserting into the VALUES clause of the SQL command.
        let paramList = [
            for i in 1..words.Length do
                let entry = words.[i-1]
                command.Parameters.AddWithValue($"@w{i}", entry.word) |> ignore
                command.Parameters.AddWithValue($"@o{i}", entry.offensiveness.Value) |> ignore
                command.Parameters.AddWithValue($"@c{i}", entry.commonness.Value) |> ignore
                command.Parameters.AddWithValue($"@s{i}", entry.sentiment.Value) |> ignore

                yield $"(@w{i}, @o{i}, @c{i}, @s{i})"
        ]

        command.CommandText <- $"""
    MERGE INTO words WITH (HOLDLOCK) AS target
    USING (
        VALUES 
            {join "," paramList}
        ) AS source(word, offensiveness, commonness, sentiment)
    ON target.word = source.word
    WHEN MATCHED THEN
        UPDATE SET 
            offensiveness = source.offensiveness, 
            commonness = source.commonness,
            sentiment = source.sentiment
    WHEN NOT MATCHED THEN
        INSERT (word, offensiveness, commonness, sentiment) 
        VALUES (source.word, source.offensiveness, source.commonness, source.sentiment);
    """

        command.CommandType <- System.Data.CommandType.Text

        // We don't need output parameters for this command, so we can use optimized 
        // parameter binding for better performance.
        command.EnableOptimizedParameterBinding <- true

        printfn($"updating database with {words.Length} word(s)")

        // If we do hit a duplicate key error, just retry as the upsert should handle it.
        let mutable retry = true
        let mutable updatedCount = 0

        while retry do
            try
                let count = command.ExecuteNonQuery()
                updatedCount <- count
                retry <- false
            with
            | :? SqlException as ex when ex.Number = 2601 || ex.Number = 2627 -> 
                printfn("Duplicate key error, retrying...")
                retry <- true
            | :? SqlException as ex ->
                printfn "SQL EXCEPTION [%d]:" ex.Number
                printfn "%s" (ex.ToString())
                raise ex
            | ex ->
                printfn "Query error: %s" (ex.ToString())
                raise ex

        printfn($"updated {updatedCount} record(s)")

        // Now update word types. There can be multiple types for a word, so they go into a separate table.
        for word in words do
            // Delete existing word types (for this word only) first.
            use deleteCommand = connection.CreateCommand()
            deleteCommand.CommandText <- $"DELETE FROM word_types WHERE word = @word"
            deleteCommand.Parameters.AddWithValue("@word", word.word) |> ignore

            let _ = deleteCommand.ExecuteNonQuery()

            // Now insert all word types.
            use insertCommand = connection.CreateCommand()
            insertCommand.Parameters.AddWithValue("@word", word.word) |> ignore
            let paramList = [
                for i in 1..word.types.Length do
                    insertCommand.Parameters.AddWithValue($"@type{i}", word.types.[i-1]) |> ignore
                    yield $"(@word, @type{i})"
            ]
            insertCommand.CommandText <- sprintf "INSERT INTO word_types (word, type) VALUES %s" (join ", " paramList)

            let _ = insertCommand.ExecuteNonQuery()
            ()

        updatedCount

/// Start the database processor waiting for messages to process.
let start config =
    let buffer = System.Collections.Generic.List<WordRecord>()
    let connection = new Microsoft.Data.SqlClient.SqlConnection(config.connectionString)
    connection.Open()

    Processor.start {
        name = "Database" 
        handler = fun _ msg -> async {
            match msg with
            | Update word ->
                buffer.Add(word)
                if buffer.Count >= config.batchSize then
                    let words = buffer.ToArray()
                    buffer.Clear()
                    words |> distinct |> updateWords connection |> ignore
        }
        stopped = fun _ _ -> async {            
            buffer.ToArray() |> distinct |> updateWords connection |> ignore
        }
        register = true
    }