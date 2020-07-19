module MyNamespace.globals

open System.Threading

let lockobj=new obj()

let zlog s=
        Monitor.Enter lockobj
        printfn "id:  | %s" s
        Monitor.Exit lockobj


let config=["127.0.0.1:12001";"127.0.0.1:12002";"127.0.0.1:12003"]