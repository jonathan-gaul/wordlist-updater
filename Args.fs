module Args

open System

type CommandLineOptions = {
    resume: bool
    reset: bool
    only: string option
    noUpdate: bool
}

let rec parseCommandLineRec args acc =
    match args with 
    | [] -> acc
    | "--resume" :: rest -> parseCommandLineRec rest { acc with resume = true }
    | "--reset" :: rest -> parseCommandLineRec rest { acc with reset = true }
    | "--only" :: value :: rest -> parseCommandLineRec rest { acc with only = Some value }
    | "--no-update" :: rest -> parseCommandLineRec rest { acc with noUpdate = true }
    | _ -> failwith "Unknown command line argument or missing value"

let defaultOptions = {
    resume = false
    reset = false
    only = None
    noUpdate = false
}

let parseCommandLine =
    let args = Environment.GetCommandLineArgs() |> Array.tail |> Array.toList
    parseCommandLineRec args defaultOptions