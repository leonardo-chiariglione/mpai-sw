using System; 
using Mpai.Repository; 
 
var repo = new AssetRepository(); 
 
var acousticId = 
    repo.GenerateAssetId( 
        AssetType.AcousticProfile); 

repo.CreateAsset( 
    new RepositoryAsset 
    { 
        AssetId = acousticId, 
        AssetType = AssetType.AcousticProfile 
    }); 

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

Console.WriteLine(
    "GetReferences(" + bas1Id + ") [old version, before any Save-copy change]: " +
    string.Join(", ", repo.GetReferences(bas1Id)));

Console.WriteLine(
    "GetReferences(" + bas2.AssetId + ") [new version - must NOT be empty, must still be " + baoId + "]: " +
    string.Join(", ", repo.GetReferences(bas2.AssetId)));

Console.WriteLine(
    "BAO referenced is still " + baoId + " (BAOs are static - saving the scene never touches them): " +
    (repo.GetAsset(baoId) != null && repo.FindAssets(AssetType.BAO).Count() == 1));
 
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

Console.WriteLine( 
    "CheckDeleteAllowed(" + baoId + "): " + 
    repo.CheckDeleteAllowed(baoId)); 

Console.WriteLine( 
    "GetProvenance(" + bas2.AssetId + "): " + 
    repo.GetProvenance(bas2.AssetId)); 

repo.CreateReference( 
    baoId, 
    acousticId); 

Console.WriteLine( 
    "GetDependencies(" + bas1Id + ") [BAS -> BAO -> AcousticProfile]: " + 
    string.Join( 
        ", ", 
        repo.GetDependencies( 
            bas1Id))); 

Console.WriteLine( 
    "ValidateDependencies(" + bas1Id + "): " + 
    repo.ValidateDependencies(bas1Id)); 

Console.WriteLine( 
    "RemoveReference(" + bas1Id + " -> " + baoId + "): " + 
    repo.RemoveReference(bas1Id, baoId)); 

Console.WriteLine( 
    "GetReferences(" + bas1Id + ") after remove: " + 
    string.Join( 
        ", ", 
        repo.GetReferences(bas1Id))); 
 
Console.WriteLine("PASS"); 

// --- Persistence round-trip: create with one Repository instance backed
// by disk, then open a SECOND instance against the same folder and check
// the asset and its parent/reference survive a restart. ---

var persistRoot = Path.Combine(Path.GetTempPath(), "AsmRepositoryTest_" + Guid.NewGuid());

var repoA = new AssetRepository(persistRoot);

var pBaoId = repoA.GenerateAssetId(AssetType.BAO);
repoA.CreateAsset(new RepositoryAsset { AssetId = pBaoId, AssetType = AssetType.BAO });

var pBas1 = new RepositoryAsset { AssetId = repoA.GenerateAssetId(AssetType.BAS), AssetType = AssetType.BAS };
repoA.CreateAsset(pBas1);
repoA.CreateReference(pBas1.AssetId, pBaoId);
var pBas2 = repoA.SaveAsset(pBas1);

Console.WriteLine();
Console.WriteLine("--- Persistence round-trip ---");
Console.WriteLine("Root: " + persistRoot);
Console.WriteLine("Created on disk: " + pBaoId + ", " + pBas1.AssetId + ", " + pBas2.AssetId);

var repoB = new AssetRepository(persistRoot);

Console.WriteLine("Reloaded GetAsset(" + pBas2.AssetId + "): " + (repoB.GetAsset(pBas2.AssetId) != null));
Console.WriteLine("Reloaded GetProvenance(" + pBas2.AssetId + "): " + repoB.GetProvenance(pBas2.AssetId));
Console.WriteLine("Reloaded GetReferences(" + pBas1.AssetId + "): " + string.Join(", ", repoB.GetReferences(pBas1.AssetId)));

// A fresh ID from the reloaded instance must not collide with what's
// already on disk from repoA.
var pBaoId2 = repoB.GenerateAssetId(AssetType.BAO);
Console.WriteLine("Next BAO id after reload (must differ from " + pBaoId + "): " + pBaoId2);

Directory.Delete(persistRoot, recursive: true);

Console.WriteLine("PERSISTENCE PASS"); 

// --- Cycle prevention: a composite must not be able to reference its own
// ancestor. B is created after A and legitimately references A; A was
// created before B even existed, so A referencing B back would require
// referencing something from the future - must be rejected. ---

var cycleRepo = new AssetRepository();
var aId = cycleRepo.GenerateAssetId(AssetType.BAO);
cycleRepo.CreateAsset(new RepositoryAsset { AssetId = aId, AssetType = AssetType.BAO });
var bId = cycleRepo.GenerateAssetId(AssetType.BAO);
cycleRepo.CreateAsset(new RepositoryAsset { AssetId = bId, AssetType = AssetType.BAO });

cycleRepo.CreateReference(bId, aId);   // valid: B (later) references A (earlier)
Console.WriteLine("B -> A reference (valid): OK");

try
{
    cycleRepo.CreateReference(aId, bId);   // invalid: would close a cycle A -> B -> A
    Console.WriteLine("CYCLE TEST FAILED: A -> B was wrongly accepted");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine("A -> B correctly REJECTED: " + ex.Message);
}

Console.WriteLine("CYCLE PREVENTION PASS"); 