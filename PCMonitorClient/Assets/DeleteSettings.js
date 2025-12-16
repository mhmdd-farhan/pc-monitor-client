var fso = new ActiveXObject("Scripting.FileSystemObject");
var filePath = "C:\\MyApps\\Nadi Monitor\\Settings.dll";

if (fso.FileExists(filePath)) {
  fso.DeleteFile(filePath, true); // true = force delete
}
