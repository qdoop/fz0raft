let _cmds=new System.Collections.Concurrent.ConcurrentDictionary<int, string>(2,1)

let b=_cmds.TryAdd(1,"xxx")
let getKey(key)=
    let b0, v=_cmds.TryGetValue(key)
    if b0 then
        Some v
    else
        None

    printfn "b0=%A" b0

let clientthread=new System.Threading.Thread( fun () ->
        let mutable i=0
        while true do
            System.Threading.Thread.Sleep(1000)
            getKey(1)
        )

clientthread.Start()      

while true do
    System.Threading.Thread.Sleep(1000)