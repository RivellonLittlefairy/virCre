using VirCre.Launcher;

static class AgentCatalogChecks {
    private const string App="\"type\":\"app\",\"package\":\"com.test.catalog\",\"name\":\"应用\",\"system\":false";
    private const string Complete="\n{\"type\":\"complete\"}\n";
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=";
    internal static async Task RunAsync() {
        var root=Path.Combine(AppContext.BaseDirectory,"agent-catalog-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);var agent=Path.Combine(root,"icon-agent.jar");await File.WriteAllTextAsync(agent,"fixture");
        try {
            foreach(var field in new[]{"",",\"png\":null",",\"png\":42",",\"png\":true",",\"png\":false",",\"png\":{}",",\"png\":[]",",\"png\":\"not base64!\""}) {
                var runner=new CatalogRunner("{"+App+field+"}"+Complete);
                var catalog=new AgentPhoneCatalog(new PhoneCatalog(runner),runner,agent,Path.Combine(root,"icons"));
                var warnings=new List<string>();catalog.Warning+=warnings.Add;
                var apps=await catalog.GetAppsAsync("phone",default);
                Check(apps.Count==1 && apps[0].PackageName=="com.test.catalog" && apps[0].IconPath==null,"Optional invalid icon discarded the app: "+field);
                Check(warnings.Count==1 && warnings[0].StartsWith("1 个图标"),"Missing icon warning count: "+field);
                Check(runner.Cleanups==1,"Catalog helper was not cleaned after optional icon failure");
            }
            var validRunner=new CatalogRunner("{"+App+",\"png\":\""+Png+"\"}"+Complete);
            var valid=new AgentPhoneCatalog(new PhoneCatalog(validRunner),validRunner,agent,Path.Combine(root,"icons"));
            int warningCount=0;valid.Warning+=_=>warningCount++;
            var normal=await valid.GetAppsAsync("phone",default);
            Check(normal.Single().IconPath is { } icon && File.ReadAllBytes(icon).SequenceEqual(Convert.FromBase64String(Png)) && warningCount==0,"Normal icon changed or unexpectedly warned");
            foreach(var required in new[]{
                "{\"type\":\"app\",\"name\":\"应用\",\"system\":false}",
                "{\"type\":\"app\",\"package\":\"com.test.catalog\",\"name\":42,\"system\":false}",
                "{\"type\":\"app\",\"package\":\"bad package\",\"name\":\"应用\",\"system\":false}",
                "{\"type\":\"app\",\"package\":\"com.test.catalog\",\"name\":\"应用\",\"system\":\"false\"}"}) {
                var runner=new CatalogRunner(required+Complete);
                var catalog=new AgentPhoneCatalog(new PhoneCatalog(runner),runner,agent,Path.Combine(root,"icons"));
                try {await catalog.GetAppsAsync("phone",default);throw new Exception("Required field error was swallowed");}
                catch(InvalidDataException error) when(CatalogErrors.IsFormatFailure(error)){}
                Check(runner.Cleanups==1,"Catalog helper was not cleaned after required field failure");
            }
            var bug=new InvalidOperationException("observer bug");
            var bugRunner=new CatalogRunner("{"+App+"}"+Complete);
            var observable=new AgentPhoneCatalog(new PhoneCatalog(bugRunner),bugRunner,agent,Path.Combine(root,"icons"));
            observable.Warning+=_=>throw bug;
            try {await observable.GetAppsAsync("phone",default);throw new Exception("Programming bug was swallowed");}
            catch(InvalidOperationException error) when(ReferenceEquals(error,bug)){}
            Console.WriteLine("PASS: Agent catalog optional icon types/base64/normal PNG, required fields, cleanup and unexpected error propagation");
        } finally {
            var resolved=Path.GetFullPath(root);
            if(!resolved.StartsWith(Path.GetFullPath(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe test cleanup path");
            Directory.Delete(resolved,true);
        }
    }
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    private sealed class CatalogRunner(string output):ICommandRunner {
        public int Cleanups {get;private set;}
        public Task<CommandResult> RunAsync(string file,IReadOnlyList<string> arguments,TimeSpan timeout,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            if(file!="adb" || arguments.Count<3 || arguments[0]!="-s" || arguments[1]!="phone")throw new InvalidOperationException("Unexpected catalog command");
            if(arguments.Contains("--catalog"))return Task.FromResult(new CommandResult(0,output,""));
            if(arguments[2]=="push")return Task.FromResult(new CommandResult(0,"",""));
            if(arguments.Contains("rm")) {Cleanups++;return Task.FromResult(new CommandResult(0,"",""));}
            throw new InvalidOperationException("Unexpected catalog phase");
        }
    }
}
