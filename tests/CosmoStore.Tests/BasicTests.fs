﻿module CosmoStore.Tests.BasicTests

open System
open NUnit.Framework
open CosmoStore
open Newtonsoft.Json.Linq
open Microsoft.Azure.Documents.Client
open CosmoStore.TableStorage

module CosmosDb =
    open CosmoStore.CosmosDb

    let smallSize = 1000
    let bigSize = 100000


    let private getConfig throughput = 
        let conf = 
            CosmoStore.CosmosDb.Configuration.CreateDefault 
                (Uri "https://jvl-cosmos.documents.azure.com") 
                "ufZTuHTcAtvTuOcn1SVrt4MzjGajtwQZT8oNxvnZs0nCj4hl8dkYnwEvewtcL7BKzIV4vWcVMcDaN8qgH0aZbw=="
        let c = if throughput > 10000 then { conf with Capacity = Unlimited; Throughput = throughput } else { conf with Throughput = throughput }
        let n = sprintf "EventStore_%i" throughput
        { c with DatabaseName = n }

    let getCleanEventStore throughput =
        let conf = getConfig throughput
        let client = new DocumentClient(conf.ServiceEndpoint, conf.AuthKey)
        try
            do client.DeleteDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(conf.DatabaseName, "Events")) 
            |> Async.AwaitTask 
            |> Async.RunSynchronously 
            |> ignore
        with ex -> ()
        conf |> EventStore.getEventStore

    let eventStoreSmall = 
        smallSize |> getCleanEventStore 
    let eventStoreBig = 
        bigSize |> getCleanEventStore

// module TableStorage =
//     open CosmoStore.TableStorage
//     open Microsoft.WindowsAzure.Storage

//     let private conf = Configuration.CreateDefaultForLocalEmulator()

//     let getCleanEventStore() =
//         let account = CloudStorageAccount.DevelopmentStorageAccount
//         let client = account.CreateCloudTableClient()
//         let table = client.GetTableReference("Events")
//         try
//             table.DeleteIfExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
//         with _ -> ()
//         conf |> EventStore.getEventStore
    
//     let eventStore = getCleanEventStore()

let getStreamId () = sprintf "TestStream_%A" (Guid.NewGuid())

let getEvent i =
    {
        Id = Guid.NewGuid()
        CorrelationId = Guid.NewGuid()
        Name = sprintf "Created_%i" i
        Data = JValue("TEST STRING")
        Metadata = JValue("TEST STRING META") :> JToken |> Some
    }

let appendEvents store streamId =
    List.map getEvent
    >> store.AppendEvents streamId ExpectedPosition.Any
    >> Async.AwaitTask
    >> Async.RunSynchronously

let checkPosition acc (item:EventRead) =
        Assert.IsTrue(item.Position > acc)
        item.Position

type StoreType =
    | CosmosSmall = 0   
    | CosmosBig = 1
    | TableStorage = 2

let getEventStore = function
    | StoreType.CosmosSmall -> CosmosDb.eventStoreSmall
    | StoreType.CosmosBig -> CosmosDb.eventStoreBig
    | StoreType.TableStorage -> CosmosDb.eventStoreSmall

let getCleanEventStore = function
    | StoreType.CosmosSmall -> CosmosDb.getCleanEventStore CosmosDb.smallSize
    | StoreType.CosmosBig -> CosmosDb.getCleanEventStore CosmosDb.bigSize
    | StoreType.TableStorage -> CosmosDb.getCleanEventStore CosmosDb.smallSize


[<Test>]
let ``Appends event`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    getEvent 1
    |> store.AppendEvent streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        Assert.AreEqual(1, er.Position)
    )

[<Test>]
let ``Get event`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let event =
        store.GetEvent streamId 3L
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(3L, event.Position)
    Assert.AreEqual("Created_3", event.Name)

[<Test>]
let ``Get events (all)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId EventsReadRange.AllEvents
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(10, events.Length)
    events |> List.fold checkPosition 0L |> ignore

[<Test>]
let ``Get events (from position)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.FromPosition(6L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(5, events.Length)
    events |> List.fold checkPosition 5L |> ignore
    
[<Test>]
let ``Get events (to position)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.ToPosition(5L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(5, events.Length)
    events |> List.fold checkPosition 0L |> ignore

[<Test>]
let ``Get events (position range)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.PositionRange(5L,7L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(3, events.Length)
    events |> List.fold checkPosition 4L |> ignore

[<Test>]
let ``Get streams (all)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let addEventToStream i =
        [1..99]
        |> List.map getEvent
        |> store.AppendEvents (sprintf "A_%i" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams StreamsReadFilter.AllStreams |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("A_1", streams.Head.Id)
    Assert.IsTrue(streams.Head.LastUpdatedUtc > DateTime.MinValue)
    Assert.AreEqual(99, streams.Head.LastPosition)
    Assert.AreEqual("A_2", streams.[1].Id)
    Assert.AreEqual("A_3", streams.[2].Id)
    
[<Test>]
let ``Get streams (startswith)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let startsWith = Guid.NewGuid().ToString("N")
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "X%i_%s" i startsWith) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.StarsWith("X2_"+startsWith)) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(sprintf "X2_%s" startsWith, streams.Head.Id)

[<Test>]
let ``Get streams (endswith)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let endsWith = Guid.NewGuid().ToString("N")
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "X%i_%s" i endsWith) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.EndsWith(endsWith)) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(3, streams.Length)
    Assert.AreEqual(sprintf "X1_%s" endsWith, streams.Head.Id)

[<Test>]
let ``Get streams (contains)`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let contains = Guid.NewGuid().ToString("N")
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "C_%s_%i" contains i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.Contains(contains)) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(3, streams.Length)
    Assert.AreEqual(sprintf "C_%s_1" contains, streams.Head.Id)

[<Test>]
let ``Fails to append to existing position`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    Assert.Throws<AggregateException>(fun _ -> 
        getEvent 1
        |> store.AppendEvent streamId ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        getEvent 1
        |> store.AppendEvent streamId (ExpectedPosition.Exact(1L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    ) 
    |> (fun x -> 
        Assert.IsTrue(x.Message.Contains("ESERROR_POSITION_POSITIONNOTMATCH"))
    )

[<Test>]
let ``Fails to append to existing stream if is not expected to exist`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()
    Assert.Throws<AggregateException>(fun _ -> 
        getEvent 1
        |> store.AppendEvent streamId ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        getEvent 1
        |> store.AppendEvent streamId ExpectedPosition.NoStream
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    ) 
    |> (fun x -> 
        Assert.IsTrue(x.Message.Contains("ESERROR_POSITION_STREAMEXISTS"))
    )

[<Test>]
let ``Appends events`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    let checkCreation acc item =
        Assert.IsTrue(item.CreatedUtc >= acc)
        item.CreatedUtc

    [1..99]
    |> List.map getEvent
    |> store.AppendEvents streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        er |> List.fold checkCreation DateTime.MinValue |> ignore
        er |> List.fold checkPosition 0L |> ignore
    )