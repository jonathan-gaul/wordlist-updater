module Args

open System

type CommandLineOptions = {
    resume: bool
    reset: bool
    only: string option
}

let rec parseCommandLineRec args acc =
    match args with 
    | [] -> acc
    | "--resume" :: rest -> parseCommandLineRec rest { acc with resume = true }
    | "--reset" :: rest -> parseCommandLineRec rest { acc with reset = true }
    | "--only" :: value :: rest -> parseCommandLineRec rest { acc with only = Some value }
    | _ -> failwith "Unknown command line argument or missing value"

let defaultOptions = {
    resume = false
    reset = false
    only = None
}

let parseCommandLine =
    let args = Environment.GetCommandLineArgs() |> Array.tail |> Array.toList
    parseCommandLineRec args defaultOptions