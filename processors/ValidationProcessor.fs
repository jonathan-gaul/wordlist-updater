module ValidationProcessor

open Util
open ValidationProcessorMessage

// ======================================================================
// Validation processor
// ----------------------------------------------------------------------
// - Receives score records (in CSV format).
// - Validates the records and their scores.
// - Sends valid words to the database processor for updating.
// ======================================================================

/// Validation processor configuration options.
type Configuration = 
    { /// Number of threads to use for processing.
      threadCount : int }

    /// Default configuration for the validation processor.
    static member empty = {
        threadCount = 5
    }

let validTypes = Set.ofArray [|
    "noun"
    "verb"
    "adjective"
    "adverb"
    "pronoun"
    "preposition"
    "conjunction"
    "interjection"
    "article"
|]

/// Validation processor configuration options.
let start config =   
    Processor.startRoundRobin config.threadCount {
        name = "Validation" 
        handler = fun self msg -> async {
            match msg with
            | Validate (record) ->
                
                let outOfRange min max value = 
                    match value with
                    | Some v when v >= min && v <= max -> false
                    | _ -> true

                try
                    match record with 
                    | x when nullOrBlank x.word || 
                        x.offensiveness |> outOfRange  0 10 ||                         
                        x.commonness |> outOfRange 0 10 || 
                        x.sentiment |> outOfRange -10 10 ->
                        printfn "Resubmitting due to invalid value in record: %s" (record.ToString())
                        LlmProcessorMessage.Process x.word |> Processor.dispatch
                    | x when x.types |> Array.forall validTypes.Contains |> not ->
                        printfn "Resubmitting due to invalid type in record: %s" (record.ToString())
                        LlmProcessorMessage.Process x.word |> Processor.dispatch
                    | x -> 
                        DbProcessorMessage.Update x |> Processor.dispatch
                with
                | ex -> 
                    printfn "Skipping due to error when validating record: %s" record.word
                    printfn "Error: %s" ex.Message
        }
        stopped = fun _ _ -> async { () }        
        register = true
    }