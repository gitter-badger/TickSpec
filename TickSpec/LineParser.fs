﻿module internal TickSpec.LineParser

open System.Text.RegularExpressions
open System.Globalization

/// Block type
type internal BlockType =
    | Named of string
    | Background
    | Shared of string option
    with
    override this.ToString() =
        match this with
        | Named s -> s
        | Background -> "Background"
        | Shared None -> "Shared Examples"
        | Shared (Some(tag)) -> "Shared Examples of " + tag

/// Item type
type internal ItemType =
    | BulletPoint of string
    | TableRow of string[]
    | DocString of string

/// Line type
type internal LineType =
    | BlockStart of BlockType
    | ExamplesStart
    | Step of StepType
    | Item of LineType * ItemType
    | TagLine of string list

/// Try single parameter regular expression
let tryRegex input pattern =
    let m = Regex.Match(input, pattern, RegexOptions.IgnoreCase)
    if m.Success then m.Groups.[1].Value |> Some
    else None

let startsWith (pattern:string) (s:string) =
    s.StartsWith(pattern, System.StringComparison.InvariantCultureIgnoreCase)

let (|Scenario|_|) (s:string) =
    let s = s.Trim()
    if s |> startsWith "Scenario" || s |> startsWith "Story" then
        Scenario s |> Some else None
let (|IsBackground|_|) s =
    tryRegex s "^\s*Background(.*)"
    |> Option.map (fun t -> IsBackground)
let (|GivenLine|_|) s =
    tryRegex s "^\s*Given\s+(.*)"
    |> Option.map (fun t -> GivenLine t)
let (|WhenLine|_|) s =
    tryRegex s "^\s*When\s+(.*)"
    |> Option.map (fun t -> WhenLine t)
let (|ThenLine|_|) s =
    tryRegex s "^\s*Then\s+(.*)"
    |> Option.map (fun t -> ThenLine t)
let (|AndLine|_|) s =
    tryRegex s "^\s*And\s+(.*)"
    |> Option.map (fun t -> AndLine t)
let (|ButLine|_|) s =
    tryRegex s "^\s*But\s+(.*)"
    |> Option.map (fun t -> ButLine t)
let (|Row|_|) (s:string) =
    if s.Trim().StartsWith("|") then
        let options = System.StringSplitOptions.RemoveEmptyEntries
        let cols = s.Trim().Split([|'|'|],options)
        let cols = cols |> Array.map (fun s -> s.Trim())
        Row cols |> Some
    else None
let (|Bullet|_|) (s:string) =
    if s.Trim().StartsWith("*") then
        s.Substring(s.IndexOf("*")+1).Trim() |> Some
    else None
let (|DocMarker|_|) (s:string) =
    if s.Trim() = "\"\"\"" then Some s
    else None
let (|SharedExamplesOf|_|) s =
    tryRegex s @"^\s*Shared\s+Examples\s+Of\s+@(.*[^:])"
    |> Option.map (fun t -> SharedExamplesOf t)
let (|SharedExamples|_|) (s:string) =
    if s.Trim() |> startsWith("Shared Examples") then Some SharedExamples else None
let (|Examples|_|) (s:string) =
    if s.Trim() |> startsWith("Examples") then Some Examples else None
let (|Attributes|_|) (s:string) =
    if s.Trim().StartsWith("@") then
        let tags =
            seq { for tag in Regex.Matches(s,@"@(\w+)") do yield tag.Value.Substring(1) }
        Attributes (tags |> Seq.toList) |> Some
    else None

/// Line state given previous line state and new line text
let parseLine = function
    | _, Scenario text -> BlockStart (Named(text)) |> Some
    | _, IsBackground -> BlockStart Background |> Some
    | _, SharedExamplesOf tag -> BlockStart (Shared(Some(tag))) |> Some
    | _, SharedExamples -> BlockStart (Shared(None)) |> Some
    | _, Examples -> ExamplesStart |> Some
    | BlockStart (Named _), GivenLine text
    | BlockStart Background, GivenLine text
    | Step(GivenStep _), GivenLine text | Item(Step(GivenStep _),_), GivenLine text
    | Step(GivenStep _), AndLine text | Item(Step(GivenStep _),_), AndLine text
    | Step(GivenStep _), ButLine text | Item(Step(GivenStep _),_), ButLine text
        -> Step(GivenStep text) |> Some
    | BlockStart (Named _), WhenLine text
    | BlockStart Background, WhenLine text
    | Step(GivenStep _), WhenLine text | Item(Step(GivenStep _),_), WhenLine text
    | Step(WhenStep _), WhenLine text | Item(Step(WhenStep _),_), WhenLine text
    | Step(WhenStep _), AndLine text | Item(Step(WhenStep _),_), AndLine text
    | Step(WhenStep _), ButLine text | Item(Step(WhenStep _),_), ButLine text
    | Step(ThenStep _), WhenLine text | Item(Step(ThenStep _),_), WhenLine text
        -> Step(WhenStep text) |> Some
    | BlockStart (Named _), ThenLine text
    | BlockStart Background, ThenLine text
    | Step(GivenStep _), ThenLine text | Item (Step(GivenStep _),_), ThenLine text
    | Step(WhenStep _), ThenLine text | Item (Step(WhenStep _),_), ThenLine text
    | Step(ThenStep _), ThenLine text | Item (Step(ThenStep _),_), ThenLine text
    | Step(ThenStep _), AndLine text | Item(Step(ThenStep _),_), AndLine text
    | Step(ThenStep _), ButLine text | Item(Step(ThenStep _),_), ButLine text
        -> Step(ThenStep text) |> Some
    | (Step(GivenStep _) as line), DocMarker xs
    | (Step(WhenStep _) as line), DocMarker xs
    | (Step(ThenStep _) as line), DocMarker xs
    | Item (line, DocString(_)), xs ->
        Item(line, DocString(xs)) |> Some
    | (Step(GivenStep _) as line), Bullet xs
    | (Step(WhenStep _) as line), Bullet xs
    | (Step(ThenStep _) as line), Bullet xs
    | Item (line, BulletPoint(_)), Bullet xs ->
        Item(line, BulletPoint xs) |> Some
    | (BlockStart (Shared(_)) as line), Row xs
    | (ExamplesStart as line), Row xs
    | (Step(GivenStep _) as line), Row xs
    | (Step(WhenStep _) as line), Row xs
    | (Step(ThenStep _) as line), Row xs ->
        Item(line, TableRow xs) |> Some
    | Item (line, TableRow ys), Row xs when ys.Length = xs.Length ->
        Item(line, TableRow xs) |> Some
    | _, Attributes values ->
        TagLine values |> Some
    | _, line -> None

let expectingLine = function
    | BlockStart (Named _) | BlockStart Background -> "Expecting Given, When or Then step"
    | BlockStart (Shared _) -> "Expecting Table row"
    | Step(GivenStep _) | Item(Step(GivenStep _),_) ->
        "Expecting Table row, Bullet, Given, When, Then, And or But step"
    | Step(WhenStep _) | Item(Step(WhenStep _),_) ->
        "Expecting Table row, Bullet, When, Then, And or But step"
    | Step(ThenStep _) | Item(Step(ThenStep _),_) ->
        "Expecting Table row, Bullet, Then, And or But step"
    | ExamplesStart -> "Expecting Table row"
    | Item(_,_) -> "Unexpected or invalid line"
    | TagLine _ -> "Unexpected line"
