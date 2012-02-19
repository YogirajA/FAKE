﻿module Fake.DeploymentHelper
    
open System
open System.IO
open System.Net
open Fake

type DeploymentResponseStatus =
| Success
| Failure of obj
| RolledBack
with 
    member x.GetError() = 
        match x with
        | Success | RolledBack -> null
        | Failure(err) -> err

type DeploymentResponse = {
        Status : DeploymentResponseStatus
        PackageName : string
    }
    with 
        static member Sucessful name =  { Status = Success; PackageName = name}
        static member RolledBack name = { Status = RolledBack; PackageName = name }
        static member Failure(name, error) = { Status = Failure error; PackageName = name}
        member x.SwitchTo(status) = { x with Status = status }

type DeploymentPushStatus = 
    | Cancelled
    | Error of exn
    | Ok of DeploymentResponse
    | Unknown

type Directories = {
    App : DirectoryInfo
    Backups : DirectoryInfo
    Active : DirectoryInfo
}

let private extractNuspecFromPackageFile packageFileName =   
    packageFileName
    |> ZipHelper.UnzipFirstMatchingFileInMemory (fun ze -> ze.Name.EndsWith ".nuspec") 
    |> NuGetHelper.getNuspecProperties

let mutable workDir = "."
let mutable deploymentRootDir = "deployments/"

let getActiveReleasesInDirectory dir = 
    !! (dir @@ deploymentRootDir @@ "**/active/*.nupkg")
      |> Seq.map extractNuspecFromPackageFile

let getActiveReleases() = getActiveReleasesInDirectory workDir

let getActiveReleaseInDirectoryFor dir (app : string) = 
    !! (dir @@ deploymentRootDir + app + "/active/*.nupkg") 
      |> Seq.map extractNuspecFromPackageFile
      |> Seq.head

let getActiveReleaseFor (app : string) = getActiveReleaseInDirectoryFor workDir app

let getAllReleasesInDirectory dir = 
    !! (dir @@ deploymentRootDir @@ "**/*.nupkg")
      |> Seq.map extractNuspecFromPackageFile

let getAllReleases() = getAllReleasesInDirectory workDir

let getAllReleasesInDirectoryFor dir (app : string) = 
    !! (dir @@ deploymentRootDir + app + "/**/*.nupkg") 
      |> Seq.map extractNuspecFromPackageFile

let getAllReleasesFor (app : string) = getAllReleasesInDirectoryFor workDir app

let getBackupFor dir (app : string) (version : string) =
    let backupFileName =  app + "." + version + ".nupkg"
    dir @@ deploymentRootDir @@ app @@ "backups"
    |> FindFirstMatchingFile backupFileName

let unpack isRollback packageBytes =
    let tempFile = Path.GetTempFileName()
    WriteBytesToFile tempFile packageBytes

    let package = extractNuspecFromPackageFile tempFile   
        
    let activeDir = workDir @@ deploymentRootDir @@ package.Id @@ "active"   
    let newActiveFilePath = activeDir @@ package.FileName

    match TryFindFirstMatchingFile "*.nupkg" activeDir with
    | Some activeFilePath ->
        let backupDir = workDir @@ deploymentRootDir @@ package.Id @@ "backups"
    
        ensureDirectory backupDir
        if not isRollback then
            MoveFile backupDir activeFilePath
    | None -> ()
    
    CleanDir activeDir
    Unzip activeDir tempFile
    File.Delete tempFile

    WriteBytesToFile newActiveFilePath packageBytes

    let scriptFile = FindFirstMatchingFile "*.fsx" activeDir
    package, scriptFile
    
let doDeployment packageName script =
    try
        let workingDirectory = DirectoryName script
        
        if FSIHelper.runBuildScriptAt workingDirectory true (FullName script) Seq.empty then 
            DeploymentResponse.Sucessful(packageName)
        else 
            DeploymentResponse.Failure(packageName, Exception("Deployment script didn't run successfully"))
    with e ->
        DeploymentResponse.Failure(packageName, e) 
              
let runDeployment (packageBytes : byte[]) =
     let package,scriptFile = unpack false packageBytes
     doDeployment package.Name scriptFile

let runDeploymentFromPackageFile packageFileName =
    try
        packageFileName
        |> ReadFileAsBytes
        |> runDeployment
    with e ->
        DeploymentResponse.Failure(packageFileName, e) 

let rollbackFor dir (app : string) (version : string) =
    try 
        let currentPackageFileName = !! (dir @@ deploymentRootDir + app + "/active/*.nupkg") |> Seq.head
        let backupPackageFileName = getBackupFor dir app version
        if currentPackageFileName = backupPackageFileName
        then DeploymentResponse.Failure(app + "." + version + ".nupkg", "Cannot rollback to currently active version")
        else 
            let package,scriptFile = unpack true (backupPackageFileName |> ReadFileAsBytes)
            (doDeployment package.Name scriptFile).SwitchTo(RolledBack)
    with
        | :? FileNotFoundException as e -> DeploymentResponse.Failure(e.FileName, sprintf "Failed to rollback to %s %s could not find package file or deployment script file ensure the version is within the backup directory and the deployment script is in the root directory of the *.nupkg file" app version)
        | _ as e -> DeploymentResponse.Failure(app + "." + version + ".nupkg", "Rollback Failed: " + e.Message)

let rollback (app : string) (version : string) = rollbackFor workDir app version

let 

let postRollback url app version =
    ()

      




let PostDeploymentPackage url packageFileName = 
    match postDeploymentPackage url packageFileName with
    | Ok(_) -> tracefn "Deployment of %s successful" packageFileName
    | Error(exn) -> failwithf "Deployment of %A failed\r\n%A" packageFileName exn
    | response -> failwithf "Deployment of %A failed\r\n%A" packageFileName response
