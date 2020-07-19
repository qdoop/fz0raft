namespace MyNamespace.Raft



type ExternState() as me =

    let eq={
        new System.Collections.Generic.IEqualityComparer<int> with
        member me.Equals(x:int, y:int)=   
            x=y
        member me.GetHashCode(x:int) = 
            x
    }

    let _dic=new System.Collections.Concurrent.ConcurrentDictionary<int, LogEntry>()    
    let _cmds=new System.Collections.Concurrent.ConcurrentDictionary<int, LogEntry>()
    
    member me.Count
        with get()=_dic.Count 
    member me.Item
        with get(key)=
            let b, v=_dic.TryGetValue(key)
            if b then
                Some v
            else
                None

    member me.ApplyCommand(index:int, logentry:LogEntry)=
        let v=me.[index]
        if v.IsSome && v.Value.cmd = logentry.cmd then
            true
        elif v.IsSome && v.Value.cmd <> logentry.cmd then
            false
        else
            let b=me.AddCommand(logentry.serial, logentry)
            b && _dic.TryAdd(index, logentry)

    member me.AddCommand(key:int, logentry:LogEntry)=
        let b=_cmds.TryAdd(key, logentry)
        let b0, v=_cmds.TryGetValue(key)
        if b0 then
            Some v
        else
            None
        printfn "b0=%A" b0
        b

    member me.CheckCommand(key:int)=
        let b1, v=_cmds.TryGetValue(key)
        if b1 then
            Some v
        else
            None

        printfn "b1=%A" b1