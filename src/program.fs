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

let mutable _lastCheckPos=0
let CheckExternState( nodes: Node list)=
    let cnts=nodes |> List.map ( fun x -> x.externstate.Count )
    let max=List.max cnts

    let check (b, (pv:LogEntry option)) (xv:LogEntry option)=
        match (b, pv, xv) with
        | (false, _, _)         -> (false, pv)
        | (b,None,None)         -> (b,pv)
        | (b,None,Some v)       -> (b,xv)
        | (b, Some v, None)     -> (b,pv)
        | (b, Some x, Some y) when x.cmd=y.cmd -> (b, pv)
        | _                     -> (false, pv)

    let mutable res=true
    for i in 1..max do
        let list=nodes |> List.map ( fun x -> x.externstate.[i] )
        let rv =list |> List.fold (  fun a x -> check a x  ) (true, None)

        res <- res && (fst rv)
    _lastCheckPos <- List.min cnts
    res



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

        assert(CheckExternState(nodes))
        ()

    0