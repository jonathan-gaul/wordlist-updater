module Config

// ======================================================================
// Configuration module
// ----------------------------------------------------------------------
// - Parses command line arguments and environment variables.
// - Provides a configuration object for the application.
// ======================================================================

open System
open System.Collections

/// Represents runtime configuration of the tool.
type CommandConfig = 
    { /// Size of the word list to process.
      llmWordListSize: int

      /// Number of database records to process in a batch.
      dbBatchSize: int

      /// Connection string for the database.
      dbConnectionString: string option

      /// API key for the LLM service.
      apiKey: string option }

    /// Default configuration for the tool.
    static member empty = 
        { llmWordListSize = 50
          dbBatchSize = 50
          dbConnectionString = None
          apiKey = None }

/// Parse command line options into a configuration object.
let rec parseCommandLineRec args config =
    match args with 
    | [] -> config
    | "--api-key" :: value :: rest -> parseCommandLineRec rest { config with apiKey = Some value }
    | "--db-batch-size" :: value :: rest -> parseCommandLineRec rest { config with dbBatchSize = int value }
    | "--db-connection-string" :: value :: rest -> parseCommandLineRec rest { config with dbConnectionString = Some value }
    | "--llm-word-list-size" :: value :: rest -> parseCommandLineRec rest { config with llmWordListSize = int value }    
    | _ :: rest -> parseCommandLineRec rest config

/// Parse environment variables into a configuration object.
let rec parseEnvVarsRec (values: (string * string) list) config =
    match values with 
    | [] -> config
    | (key, value) :: rest ->
        match key.ToUpper() with 
        | "DB_CONNECTION_STRING" -> parseEnvVarsRec rest { config with dbConnectionString = Some value }
        | "CHATGPT_API_KEY" -> parseEnvVarsRec rest { config with apiKey = Some value }
        | "LLM_WORD_LIST_SIZE" ->
            match Int32.TryParse(value) with 
            | true, num -> { config with llmWordListSize = num }
            | _ -> config
        | "DB_BATCH_SIZE" ->
            match Int32.TryParse(value) with 
            | true, num -> { config with dbBatchSize = num }
            | _ -> config
        | _ -> parseEnvVarsRec rest config

let parseEnvVars =
    let values = 
        Environment.GetEnvironmentVariables() 
        |> Seq.cast<DictionaryEntry>
        |> Seq.map (fun x -> (string x.Key, string x.Value))
        |> Seq.toList
    parseEnvVarsRec values CommandConfig.empty

let parseCommandLine config =
    let args = Environment.GetCommandLineArgs() |> Array.tail |> Array.toList
    parseCommandLineRec args <| Option.defaultValue CommandConfig.empty config

/// Parse command line arguments and environment variables into a configuration object.
let parseConfig =
    parseEnvVars |> Some |> parseCommandLine