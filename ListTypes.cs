using System.Reflection;
var dll = Assembly.LoadFrom("/Users/dvanwinkle/.nuget/packages/jellyfin.controller/10.11.8/lib/net9.0/MediaBrowser.Controller.dll");
foreach (var t in dll.GetTypes())
{
    if (t.Name.Contains("GenericEvent") || (t.Name == "User" && !t.Name.Contains("Data")))
        Console.WriteLine(t.FullName);
}
