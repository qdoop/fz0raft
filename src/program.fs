open MyNamespace.Raft

open System.Net
open System.Net.Sockets
open System.Diagnostics
open Newtonsoft.Json
open System.Threading

open MyNamespace.globals
open MyNamespace.dblogger
open MyNamespace.dbviewer
// let config=["127.0.0.1:12001";"127.0.0.1:12002";"127.0.0.1:12003"] //see global.fs



[<EntryPoint>]
let main argv = 
    printfn "\n---------------------\nstarted...  args= %A  %A\n\n" argv System.DateTime.UtcNow


    // let config=["127.0.0.1:12001";]

    zdblogclear()

    let nodes= config |> List.mapi (fun i x -> new Node(string (i+1), x,config))
    let nodesrunning =nodes |> List.map ( fun(x) -> x.Start() )

    let client = new ClientNode("127.0.0.1:12009", config) 

    dbviewer nodes

    while true do
        Thread.Sleep(10_000)
        // printfn "dbsizes %A" ( zdblogGetSizes() )
        // printfn "dbmaxstamps %A" ( zdblogGMaxStamps() )
        // printfn "dbwindow %A" ( JsonConvert.SerializeObject(zdblogWindow 0 10000) )
        ()
    0