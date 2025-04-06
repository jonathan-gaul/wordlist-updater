module Util

open System

let nullOrBlank str = String.IsNullOrWhiteSpace(str)
let trim (str: string) = str.Trim()
let split (sep: string) (str: string) = str.Split([|sep|], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
let join (sep: string) (str: string seq) = String.Join(sep, str)
let lower (str : string) = str.ToLower()
