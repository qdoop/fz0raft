open MyNamespace.Raft

open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading

open MyNamespace.globals
open MyNamespace.dblogger
// let config=["127.0.0.1:12001";"127.0.0.1:12002";"127.0.0.1:12003"] //see global.fs



[<EntryPoint>]
let main argv = 
    printfn "\n---------------------\nstarted...  args= %A  %A\n\n" argv System.DateTime.UtcNow
    printfn "log dbs %A" dbcollections

    // let config=["127.0.0.1:12001";]
    let nodes= config |> List.map (fun x -> new Node(x.Split(":").[1], x,config))
    let nodesrunning =nodes |> List.map ( fun(x) -> x.Start() ) 



    Thread.Sleep(10000000)
    0