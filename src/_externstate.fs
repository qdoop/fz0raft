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
    let _cmds=new System.Collections.Concurrent.ConcurrentDictionary<System.Guid, LogEntry>()
    
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
            let b=me.AddCommand(logentry.uid, logentry)
            b && _dic.TryAdd(index, logentry)

    member me.AddCommand(key, logentry:LogEntry)=
        let b=_cmds.TryAdd(key, logentry)
        b

    member me.CheckCommand(key)=
        let b, v=_cmds.TryGetValue(key)
        if b then
            Some v
        else
            None