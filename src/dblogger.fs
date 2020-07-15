module MyNamespace.dblogger

open LiteDB

open MyNamespace.globals

type DbLogPOCO(id:int,stamp:int,json) =
    member val Id = id with get,set
    member val stamp = stamp with get,set
    member val log = json with get,set


let dbcollections= config |> List.map ( fun x ->
        let conn = @"Filename=logsnode" + x.Substring(x.Length - 1) + ".db;Connection=shared"
        let db=new LiteDatabase(conn)
        let col = db.GetCollection<DbLogPOCO>("zlogs");
        let res=col.DeleteAll()

        let e0=new DbLogPOCO(0,0,"{xxxxx}")
        let e1=new DbLogPOCO(0,0,"{xxxxx}")

        let res = col.Insert(e0)
        let res = col.Insert(e1)

        let res = col.Count()
        printfn "%A" res

        let res = col.Find((fun x -> x.Id<5), 1, 1)
        printfn "%A" res


        let res=col.DeleteAll()
        // printfn "%A" res

        col //NOTE Returns the collection NOT the db
    )

let zdblog (id:string) stamp json =
    let res=dbcollections.[(int id)-1].Insert( new DbLogPOCO(0,stamp,json) )
    ()
