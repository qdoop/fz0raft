namespace MyNamespace.Raft



type ExternState() as me =

    let _dic=new System.Collections.Concurrent.ConcurrentDictionary<int, LogEntry>()
    
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
            _dic.TryAdd(index, logentry)

