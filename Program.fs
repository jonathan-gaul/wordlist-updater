// For more information see https://aka.ms/fsharp-console-apps

open FSharp.Data
open System
open System.Text.Json
open FSharp.Control
open Microsoft.Data.SqlClient

type Word = {
    Word: string
    Offensiveness: int
    Commonness: int
    Sentiment: int
    Types: string array
}

type GPTContent = {
    ``type``: string
    text: string
    annotations: string array
}

type GPTOutput = {
    ``type``: string
    id: string
    status: string
    role: string
    content: GPTContent array
}

type GPTResponse = {
    id: string
    ``type``: string
    object: string
    status: string
    error: string
    model: string
    output: GPTOutput array
}

let nullOrBlank str = String.IsNullOrWhiteSpace(str)
let trim (str: string) = str.Trim()
let split (sep: string) (str: string) = str.Split([|sep|], StringSplitOptions.RemoveEmptyEntries)
let join (sep: char) (str: string seq) = String.Join(sep, str)

let getWordList (startWord: string option) = 
    asyncSeq {
        let! response = Http.AsyncRequestStream "https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt"
        use reader = new System.IO.StreamReader(response.ResponseStream)

        let mutable started = startWord.IsNone

        printfn("Started streaming word list...")
        while not reader.EndOfStream do
            let! line = reader.ReadLineAsync() |> Async.AwaitTask            
            let line = trim line

            if not started && line >= startWord.Value then 
                started <- true

            if started && not (nullOrBlank line) then
                yield line
    }

let getDatabaseConnection () = 
    let connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    new SqlConnection(connectionString)

let getWordScores (words: string array) = async {
    let apiKey = Environment.GetEnvironmentVariable("CHATGPT_API_KEY")
    let model = "gpt-4o-mini"

    printfn($"Getting word scores for {words[0]} - {words[words.Length-1]}")

    let! response = 
        try
            Http.AsyncRequestString(
                $"https://api.openai.com/v1/responses", 
                httpMethod = "POST",
                headers = [ 
                    "Authorization", $"Bearer {apiKey}"
                    "Content-Type", "application/json"
                ],
                body = TextRequest (JsonSerializer.Serialize {|                

                    input = $"""
For each of the following words, rate them on offensiveness and commonness (each on a scale from 0 to 10) and sentiment (on a scale from -10 to +10), and provide the word type.
Format the response strictly as CSV with no extra details or spacing—no explanations, headers, or extra text. 
Each word should be listed on its own line in the order given. 
The columns must appear exactly in this order: Word, Offensiveness, Commonness, Sentiment, Word Type. 
Provide only the raw CSV output. Do not include any quotes or formatting.
The words are: {join ',' words}
"""
                    model = model
                |}))
        with
        | ex -> 
            printfn($"failed to request scores: {ex.Message}")
            async { return "" }

    return 
        if nullOrBlank response then
            [||]
        else
            response
            |> JsonSerializer.Deserialize<GPTResponse>
            |> fun x -> x.output[0].content[0].text
            |> split "\n"
            |> Array.map (split ",")
            |> Array.map (fun x -> 
                try 
                    Some {
                        Word = x.[0] |> trim
                        Offensiveness = int x.[1]
                        Commonness = int x.[2]
                        Sentiment = int x.[3]
                        Types = x.[4].Split '/' |> Array.map trim
                    }
                with 
                | ex ->
                    printfn($"Skipping due to invalid value in record: {join ',' x}")
                    None)
            |> Array.choose id
            |> Array.filter (fun x -> x.Offensiveness >= 0 && x.Commonness >= 0 && not (nullOrBlank x.Word))
}

let updateWordsInDatabase (connection: SqlConnection) (words: Word array) = async {
    use command = connection.CreateCommand()    
    
    let paramList = [
        for i in 1..words.Length do
            let entry = words.[i-1]
            command.Parameters.AddWithValue($"@w{i}", entry.Word) |> ignore
            command.Parameters.AddWithValue($"@o{i}", entry.Offensiveness) |> ignore
            command.Parameters.AddWithValue($"@c{i}", entry.Commonness) |> ignore
            command.Parameters.AddWithValue($"@s{i}", entry.Sentiment) |> ignore

            yield $"(@w{i}, @o{i}, @c{i}, @s{i})"
    ]

    command.CommandText <- $"""
MERGE INTO words WITH (HOLDLOCK) AS target
USING (
    VALUES 
        {join ',' paramList}
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
    command.EnableOptimizedParameterBinding <- true

    printfn($"updating database with {words.Length} word(s)")

    // If we hit a duplicate key error, just retry as the upsert should handle it.
    let mutable retry = true
    let mutable updatedCount = 0

    while retry do
        try
            let! count = command.ExecuteNonQueryAsync() |> Async.AwaitTask
            updatedCount <- count
            retry <- false
        with
        | :? SqlException as ex when ex.Number = 2601 || ex.Number = 0x80131904 -> 
            printfn("Duplicate key error, retrying...")
            retry <- true
        | :? SqlException as ex ->
            printfn "SQL EXCEPTION [%d]:" ex.Number
            printfn "%s" (ex.ToString())
            raise ex
        | :? AggregateException as ex ->
            match ex.InnerException with
            | :? SqlException as sqlEx when sqlEx.Number = 2601 || sqlEx.Number = 0x80131904 ->
                printfn("Duplicate key error, retrying...")
                retry <- true
            | iex -> 
                raise iex

    printfn($"updated {updatedCount} record(s)")

    // Now update word types. There can be multiple types for a word, so they go into a separate table.
    for word in words do
        use deleteCommand = connection.CreateCommand()
        deleteCommand.CommandText <- $"DELETE FROM word_types WHERE word = @word"
        deleteCommand.Parameters.AddWithValue("@word", word.Word) |> ignore

        let! _ = deleteCommand.ExecuteNonQueryAsync() |> Async.AwaitTask

        use insertCommand = connection.CreateCommand()
        insertCommand.Parameters.AddWithValue("@word", word.Word) |> ignore
        let paramList = [
            for i in 1..word.Types.Length do
                insertCommand.Parameters.AddWithValue($"@type{i}", word.Types.[i-1]) |> ignore
                yield $"(@word, @type{i})"
        ]
        insertCommand.CommandText <- $"INSERT INTO word_types (word, type) VALUES {join ',' paramList}"

        let! _ = insertCommand.ExecuteNonQueryAsync() |> Async.AwaitTask
        ()

    return updatedCount
}

let options = Args.parseCommandLine

printfn("Connecting to database")
let connection = getDatabaseConnection()
connection.Open()
printfn("Connected to database")

if options.reset then
    printfn("Resetting database")
    use command = connection.CreateCommand()
    command.CommandText <- "TRUNCATE TABLE words; TRUNCATE TABLE word_types;"
    command.ExecuteNonQuery() |> ignore
    printfn("Database reset")

printfn("Processing word list")

// Limit the number of concurrent requests to the API to 5
let semaphore = new System.Threading.SemaphoreSlim(5)

let runLimitedAsync (workflow: Async<'T>) = async {
    do! semaphore.WaitAsync() |> Async.AwaitTask
    try
        let! result = workflow
        return result
    finally
        semaphore.Release() |> ignore
}

let updated = 
    if options.noUpdate then         
        printfn("No update requested, skipping database update")
        0
    else 
        if options.only.IsSome then        
            printfn($"Only processing {options.only.Value}")
            let scores = getWordScores [| options.only.Value |] |> Async.RunSynchronously
            updateWordsInDatabase connection scores |> Async.RunSynchronously
        else 
            let startWord = 
                if options.resume then
                    printfn("Retrieving last word from database...")
                    use lastCommand = connection.CreateCommand()
                    lastCommand.CommandText <- "SELECT TOP 1 word FROM words ORDER BY word DESC"
                    use reader = lastCommand.ExecuteReader()
                    if reader.Read() then
                        let word = reader.GetString(0)
                        printfn($"Resuming from {word}")
                        Some (word)
                    else
                        None
                else
                    None
    
            startWord
            |> getWordList 
            |> AsyncSeq.bufferByCount 50
            |> AsyncSeq.mapAsyncParallel (fun chunk -> runLimitedAsync (async {
                printfn($"processing chunk with size {chunk.Length}")
                let! scores = getWordScores chunk
                return! updateWordsInDatabase connection scores
            }))
            |> AsyncSeq.sum
            |> Async.RunSynchronously

connection.Close()
printfn($"Updated {updated} record(s)")