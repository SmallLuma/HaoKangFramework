﻿open HaoKangFramework
open System
open System.Net
open System.IO
open HaoKangFramework
open Utils
open System.Threading

let usableSpiders =
    Spider.Spiders
    |> Seq.map (fun kv -> kv.Key,kv.Value)
    |> Seq.toArray

printfn "Usable spiders:"
usableSpiders
|> Array.iteri (fun i (name,_) ->
    printfn "%d.%s" (i+1) name)
printfn "Select your spiders(split by space):"

let spiders =
    let selectedSpiders =
        Console.ReadLine().Trim().Split ' '
        |> Array.map (fun x -> 
            usableSpiders.[x |> int |> (+) -1] |> snd)
    printfn "Testing..."
    selectedSpiders
    |> Seq.map (fun spd ->
        async {
            return Spider.TestConnection spd,spd })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.filter (fun (a,_) -> a = Ok ())
    |> Array.map snd
printfn ""

printfn "Using spiders:"
spiders
|> Array.iter (printfn "%A")
printfn ""

printfn "Input your search tags(split by space):"
let tags =
    Console.ReadLine().Trim()
Console.Title <- tags
let splitedTags =
    tags.Split ' '
    |> Array.toList

let dir =
    Directory.CreateDirectory "Download" |> ignore
    match tags with
    | "" ->
        Directory.CreateDirectory("Download/no_tags").FullName + "\\"
    | tags ->
        Directory.CreateDirectory("Download/" + tags).FullName + "\\"

let logFile =
    match tags with
    | "" -> "Download/no_tags.log"
    | x -> "Download/"+x+".log"
File.Delete logFile

let Log (x:string) =
    (fun () ->
        use logFile = File.Open (logFile,FileMode.Append)
        use stream = new StreamWriter (logFile)
        stream.WriteLine x)
    |> lock logFile

let csvFile,downloadedPosts =
    let fs =
        let fileName =
            match tags with
            | "" -> "Download/no_tags.csv"
            | x -> "Download/"+x+".csv"
        
        let donwloaded =
            try
                System.IO.Directory.GetFiles ("Download")
                |> Array.filter (fun x -> x.ToLower().EndsWith ".csv")
                |> Array.collect (fun fileName -> 
                    System.IO.File.ReadAllLines fileName
                    |> Array.filter (String.IsNullOrWhiteSpace >> not)
                    |> Array.map (fun x -> 
                        let c = x.Split ','
                        c.[0],(c.[1] |> uint64)))
            with _ -> Array.empty
        
        System.IO.File.Open (fileName,FileMode.Append),donwloaded
    new StreamWriter(fst fs),snd fs

printfn "Downloaded images:%d" downloadedPosts.Length
printfn "Press any key to start..."
System.Console.ReadKey () |> ignore

let consoleLock = obj()
try
    printfn "======================================="

    let DownloadPage (page:Result<Page,exn>) = 
        let DownloadPost post = 
            let DownloadContent content = async {
                try
                    (fun () -> printfn "Downloading %s" content.FileName)
                    |> lock consoleLock

                    let fileName = 
                        let org = dir + (NormalizeFileName content.FileName)
                        if org.Length > 247 then
                            dir + (string (org.GetHashCode())) + "." + content.FileExtName
                        else
                            org
                    
                    if File.Exists fileName |> not then
                        match content.Data.Force() with
                        | Error e -> raise e
                        | Ok data ->
                            File.WriteAllBytes (fileName,data)

                            (fun () ->
                                sprintf
                                    "%A,%d,%s,%s,%A,%A,%s" 
                                    post.FromSpider
                                    post.ID
                                    content.FileName
                                    post.PostUrl 
                                    post.Score
                                    post.AgeGrading
                                    post.Author
                                |> csvFile.WriteLine
                                csvFile.Flush ())
                            |> lock csvFile
                            (fun () ->
                                printfn "Downloaded! %s" content.FileName)
                            |> lock consoleLock
                with e ->
                    sprintf @"Error:
                    Page:%A
                    Post:%A
                    Content:%A
                    Exception:%A
                    "
                        page
                        post
                        content
                        e
                    |> Log }
        
            if Array.contains (post.FromSpider |> string,post.ID) downloadedPosts |> not then
                post.Content
                |> List.map DownloadContent
                |> Async.Parallel
                |> Async.Ignore
            else
                async { return () }
        


        match page with
        | Ok page ->
            page
            |> Seq.map DownloadPost
            |> Async.Parallel
            |> Async.RunSynchronously
            |> ignore
        | Error e -> Log e.Message


    spiders
    |> Array.map (Spider.Search splitedTags)
    |> Array.map (fun spiderResult ->
        spiderResult
        |> Seq.takeWhile (fun pageResult ->
            match pageResult with
            | Ok x -> Seq.isEmpty x |> not
            | Error e -> 
                sprintf "Pages error:%A" e
                |> Log
                true))
    |> Array.Parallel.iter (Seq.iter DownloadPage)
    printfn "=============== Finished! ==============="

with ex ->
    let msg =
        sprintf @"
        致命错误：
        %A"
            ex
    Log msg
    printfn "%s" msg

csvFile.Close()
Console.Beep()
Console.ReadKey () |> ignore
