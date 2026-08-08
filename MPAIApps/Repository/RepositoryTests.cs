using System; 
using ASM.RepositoryCore; 
 
var repo = new Repository(); 
 
var baoId = 
    repo.GenerateAssetId( 
        AssetType.BAO); 
 
var bas1Id = 
    repo.GenerateAssetId( 
        AssetType.BAS); 
 
repo.CreateAsset( 
    new RepositoryAsset 
    { 
        AssetId = baoId, 
        AssetType = AssetType.BAO 
    }); 
 
var bas1 = 
    new RepositoryAsset 
    { 
        AssetId = bas1Id, 
        AssetType = AssetType.BAS 
    }; 
 
repo.CreateAsset(bas1); 
 
repo.CreateReference( 
    bas1Id, 
    baoId); 
 
var bas2 = 
    repo.SaveAsset(bas1); 
 
Console.WriteLine( 
    "BAO ID: " + baoId); 
 
Console.WriteLine( 
    "Original BAS: " + 
    bas1.AssetId); 
 
Console.WriteLine( 
    "Saved BAS: " + 
    bas2.AssetId); 
 
Console.WriteLine( 
    "Parent BAS: " + 
    bas2.ParentAssetId); 
 
Console.WriteLine( 
    "Ancestors(" + 
    bas2.AssetId + 
    "): " + 
    string.Join( 
        ", ", 
        repo.GetAncestors( 
            bas2.AssetId))); 
 
try 
{ 
    repo.DeleteAsset(baoId); 
} 
catch (Exception ex) 
{ 
    Console.WriteLine( 
        "Delete " + baoId + ": " + 
        ex.Message); 
} 
 
Console.WriteLine("PASS"); 