﻿module LlmProcessor

open FSharp.Data
open System.Text.Json
open Util

// ======================================================================
// LLM processor
// ----------------------------------------------------------------------
// - Receives words one at a time.
// - Sends them to the LLM for scoring.
// - Sends the results to the validation processor.
// ======================================================================

/// Message which can be handled by the LLM processor.
type Message =
    /// Process a single word
    | Process of string 
    
/// LLM processor configuration options.
type Configuration =
    { /// Number of words to send to the LLM at once. 
      batchSize: int

      /// API key for the LLM service.
      apiKey: string

      /// Model to use for the LLM.
      model: string

      /// Number of threads to use for processing.
      threadCount: int }

    /// Default configuration for the LLM processor.
    static member empty = {
        batchSize = 50
        apiKey = ""
        model = "gpt-4o-mini"
        threadCount = 5
    }

/// ChatGPT response object structure.
module GPT = 
    type Content = {
        ``type``: string
        text: string
        annotations: string array
    }

    type Output = {
        ``type``: string
        id: string
        status: string
        role: string
        content: Content array
    }

    type Response = {
        id: string
        ``type``: string
        object: string
        status: string
        error: string
        model: string
        output: Output array
    }

/// This is the prompt used to score the words.
// Note: LLMs can be influenced by line breaks as they are interpreted as logical grouping.
let prompt = """You will be given a list of words.  For each word, generate a line containing the following columns separated by commas:
	- the word
	- offensiveness (on a scale from 0 to 10 as a whole number, where 0 is completely inoffensive and 10 is extremely offensive)
	- commonness (on a scale from 0 to 10 as a whole number, where 0 is barely used, and 10 is extremely commonly used)
	- sentiment (on a scale from -10 to 10 as a whole number, where 0 is neutral)
	- word types (separated by / character, each word type must be a grammatically correct word type)
Do not add headers, explanations, extra spaces or any formatting to the output.  Do not include any formatting. All words provided must be processed and listed in sequence. No words may be skipped or left out. Each word must only be listed once."""

/// Score words by passing them to the LLM and returning the result as a list of strings.
/// No attempt is made to validate the result (this will be handled by the validation processor).
let scoreWords (config : Configuration) (words : string array) = async {

    if words.Length = 0 then
        return []
    else 
        let wordRange = sprintf "%s - %s" words.[words.Length - 1] words.[0]
    
        printfn "[%s] Scoring words..." wordRange
        let response = 
            try
                Http.RequestString(
                    $"https://api.openai.com/v1/responses", 
                    httpMethod = "POST",
                    headers = [ 
                        "Authorization", sprintf "Bearer %s" config.apiKey
                        "Content-Type", "application/json"
                    ],
                    body = TextRequest (JsonSerializer.Serialize {|                                    
                        input = sprintf "%s The words are: %s" prompt (join ", " words)
                        model = config.model
                    |}))
            with
            | ex -> 
                printfn "[%s] failed to request scores: %s" wordRange ex.Message
                ""

        if nullOrBlank response then
            printfn "[%s] no scores received" wordRange
            return []
        else
            let scoreLines = 
                response
                |> JsonSerializer.Deserialize<GPT.Response>
                |> fun x -> x.output[0].content[0].text
                |> split "\n"

            printfn "[%s] retrieved %d records" wordRange scoreLines.Length
            return scoreLines
}

/// Start the LLM processor waiting for messages to process.
let start config validationProcessor =
    let buffer = System.Collections.Generic.List<string>()

    Processor.startRoundRobin config.threadCount {
        name = "LLM" 
        handler = fun msg -> async {
            match msg with
            | Process word -> 
                buffer.Add(word)
            
                if buffer.Count >= config.batchSize then
                    let words = buffer.ToArray()
                    buffer.Clear()
                    let! lines = scoreWords config words
                    lines |> List.iter (fun line -> ValidationProcessor.Validate line |> Processor.dispatch validationProcessor)
        }
        shutdown = fun withChildren -> async {
            let! lines = buffer.ToArray() |> scoreWords config 
            lines |> List.iter (fun line -> ValidationProcessor.Validate line |> Processor.dispatch validationProcessor)

            if withChildren then
                printfn "Stopping LLM Processor children..."
                Processor.stop validationProcessor true |> Async.RunSynchronously
                printfn "Stopped LLM Processor children."
        }
    }