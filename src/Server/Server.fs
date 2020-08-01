open ElectronNET.API

open System.IO
open System.Threading.Tasks

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open FSharp.Control.Tasks.V2
open Giraffe
open Saturn
open Shared
open Microsoft.AspNetCore.Hosting
open ElectronNET.API.Entities


let tryGetEnv key = 
    match Environment.GetEnvironmentVariable key with
    | x when String.IsNullOrWhiteSpace x -> None 
    | x -> Some x

let publicPath = Path.GetFullPath "www"

let port =
    "SERVER_PORT"
    |> tryGetEnv |> Option.map uint16 |> Option.defaultValue 8085us

let webApp = router {
    get "/api/init" (fun next ctx ->
        task {
            let counter = {Value = 42}
            return! json counter next ctx
        })
}


module Electron =

    open Saturn
    open ElectronNET.API
    open FSharp.Control.Tasks.V2.ContextInsensitive


    type Saturn.Application.ApplicationBuilder with

        [<CustomOperation("use_electron")>]
        member __.UseElectronNet(state:ApplicationState, alternateUrl:string) =
            // check if is run by the electron cli
            let hasElectronParams =
                state.CliArguments
                |> Option.map (fun args -> args |> Array.exists (fun a -> a.Contains("ELECTRON",StringComparison.InvariantCultureIgnoreCase)))
                |> Option.defaultValue false

            let webHostConfig (cfg:IWebHostBuilder) =
                if hasElectronParams then
                    cfg.UseElectron(Option.toObj state.CliArguments)
                else
                    cfg.UseUrls(alternateUrl)

            {
                state with
                    WebHostConfigs = webHostConfig::state.WebHostConfigs
            }

        [<CustomOperation("run_electron")>]
        member __.RunElectron(state:ApplicationState, windowOptions:BrowserWindowOptions, title:string) =

            let appBuildConfig (app:IApplicationBuilder) =
                let runElectron () =
                    task {
                        try
                            let! browserWindow = Electron.WindowManager.CreateWindowAsync(windowOptions)
                            do! browserWindow.WebContents.Session.ClearCacheAsync()

                            browserWindow.add_OnReadyToShow(fun () ->
                                browserWindow.Show()
                            )

                            browserWindow.SetTitle(title)
                        with
                        | _  as ex ->
                            printfn "%s" ex.Message
                    }

                if HybridSupport.IsElectronActive then
                    runElectron () |> ignore

                app

            {
                state with
                    AppConfigs = appBuildConfig::state.AppConfigs
            }


open Electron
open ElectronNET.API.Entities


[<EntryPoint>]
let main args =

    let app = application {
        cli_arguments args
        use_electron ("http://0.0.0.0:" + port.ToString() + "/")
        run_electron (BrowserWindowOptions(Width=1200,Height=940,Show=false)) "fancy safe stack in electron net"
    
        use_router webApp
        memory_cache
        use_static publicPath
        use_json_serializer(Thoth.Json.Giraffe.ThothSerializer())
        use_gzip
    }

    run app
    0

