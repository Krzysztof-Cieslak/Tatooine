module Tatooine

open Giraffe
open Saturn
open System.Collections.Generic
open System.Text
open System.IO
open Microsoft.FSharp.Compiler.Interactive.Shell
open System.Globalization


let handlers = Dictionary<string, HttpHandler>()
type AddApiRequest = {
    name: string
    content: string
}

let getOpen path =
    let path = Path.GetFullPath path
    let filename = Path.GetFileNameWithoutExtension path
    let textInfo = (CultureInfo("en-US", false)).TextInfo
    textInfo.ToTitleCase filename

let getLoad path =
    let path = Path.GetFullPath path
    path.Replace("\\", "\\\\")


let evaluate (path: string) =
    let sbOut = StringBuilder()
    let sbErr = StringBuilder()

    let fsi =
        let inStream = new StringReader("")
        let outStream = new StringWriter(sbOut)
        let errStream = new StringWriter(sbErr)
        try
            let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
            let argv = [| "/temp/fsi.exe"; |]
            FsiEvaluationSession.Create(fsiConfig, argv, inStream, outStream, errStream)
        with
        | ex ->
            printfn "Error: %A" ex
            printfn "Inner: %A" ex.InnerException
            printfn "ErrorStream: %s" (errStream.ToString())

            raise ex

    let filename = getOpen path
    let load = getLoad path

    let _, errs = fsi.EvalInteractionNonThrowing(sprintf "#r \"%s\";;" "Giraffe.dll")
    if errs.Length > 0 then printfn "R Erros : %A" errs

    let _, errs = fsi.EvalInteractionNonThrowing(sprintf "#r \"%s\";;" "Saturn.dll")
    if errs.Length > 0 then printfn "R Erros : %A" errs

    let _, errs = fsi.EvalInteractionNonThrowing(sprintf "#load \"%s\";;" load)
    if errs.Length > 0 then printfn "Load Erros : %A" errs

    let _, errs = fsi.EvalInteractionNonThrowing(sprintf "open %s;;" filename)
    if errs.Length > 0 then printfn "Open Erros : %A" errs
    let _, errs = fsi.EvalInteractionNonThrowing(sprintf "open %s;;" "Giraffe")
    if errs.Length > 0 then printfn "Open Erros : %A" errs

    let res,errs = fsi.EvalExpressionNonThrowing "handler : HttpHandler"
    if errs.Length > 0 then printfn "Get handler Errors : %A" errs

    match res with
    | Choice1Of2 (Some f) ->
        f.ReflectionValue :?> HttpHandler |> Some
    | _ -> None


let topRouter = scope {
    get "/" (text "Hello world")
    post "/addapi" (fun f ctx ->
        task {
            let! req = ctx |> Controller.getJson<AddApiRequest>
            let path = req.name + ".fsx"
            File.WriteAllText(path, "#r \"Giraffe.dll\"\n#r \"Saturn.dll\"\nopen Giraffe\nopen Saturn\n" + req.content)
            return!
                match evaluate path with
                | None ->
                    RequestErrors.NOT_FOUND "coudln't create handler" f ctx
                | Some h ->
                    handlers.[req.name] <- h
                    Successful.OK "API added" f ctx
        }
    )
    forwardf "/api/%s" (fun s ->
        match handlers.TryGetValue s with
        | true, h -> h
        | _ -> ServerErrors.NOT_IMPLEMENTED "error" )
}

let app = application {
    router topRouter
    url "http://localhost:8085"
}


[<EntryPoint>]
let main argv =
    app |> run
    0 // return an integer exit code
