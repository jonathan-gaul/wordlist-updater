module LlmProcessor

open FSharp.Data
open System.Text.Json
open Util
open LlmProcessorMessage
open Models

// ======================================================================
// LLM processor
// ----------------------------------------------------------------------
// - Receives words one at a time.
// - Sends them to the LLM for scoring.
// - Sends the results to the validation processor.
// ======================================================================

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
	- word types (separated by / character, each word type must be one of noun, verb, adjective, adverb, pronoun, preposition, conjunction, interjection, article)
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
            let scoredWords = 
                response
                |> JsonSerializer.Deserialize<GPT.Response>
                |> fun x -> x.output[0].content[0].text
                |> split "\n"
                |> List.map (fun x -> 
                    let items = x |> split ","

                    let item index =
                        if items.Length > index then
                            Some items.[index]
                        else
                            None
                    
                    match item 0 with 
                    | Some word when word.Length > 0 -> 
                        Some { WordRecord.empty with
                                word = word
                                offensiveness = item 1 |> Option.map int
                                commonness = item 2 |> Option.map int
                                sentiment = item 3 |> Option.map int
                                types = item 4 |> Option.defaultValue "" |> split "/" |> List.map trim |> List.map lower |> Array.ofList
                            }
                    | _ -> None)
                |> List.choose id
                |> List.filter (fun x -> 
                    let result = words |> Array.contains x.word
                    if not result then
                        printfn "Removing %s from LLM Processor output as it was not in the input" x.word
                    result)

            // Repost any words that weren't retrieved to the LLM processor for another attempt.
            words 
            |> Array.except (scoredWords |> List.map (fun x -> x.word))
            |> Array.iter (fun word -> 
                printfn "Resubmitting %s to LLM Processor as it was not included in the output" word
                LlmProcessorMessage.Process word |> Processor.dispatch)            

            printfn "[%s] retrieved %d records" wordRange words.Length
            return scoredWords
}

/// Start the LLM processor waiting for messages to process.
let start config =
    let buffer = System.Collections.Generic.List<string>()

    Processor.startRoundRobin config.threadCount {
        name = "LLM" 
        handler = fun _ msg -> async {
            match msg with
            | Process word -> 
                buffer.Add(word)
            
                if buffer.Count >= config.batchSize then
                    let words = buffer.ToArray()
                    buffer.Clear()
                    let! words = words |> scoreWords config
                    words |> List.iter (fun word -> ValidationProcessorMessage.Validate word |> Processor.dispatch)
        }
        stopped = fun _ _ -> async {
            let! lines = buffer.ToArray() |> scoreWords config
            lines |> List.iter (fun line -> ValidationProcessorMessage.Validate line |> Processor.dispatch)
        }
        register = true
    }