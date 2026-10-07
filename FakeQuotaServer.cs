using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

// Test-only CLI replacement. No account/config/network access.
static class FakeQuotaServer {
    static void Main() {
        var folder=AppDomain.CurrentDomain.BaseDirectory;
        string mode=File.ReadAllText(Path.Combine(folder,"fake-quota-mode.txt"));
        var json=new JavaScriptSerializer();
        var initialize=json.Deserialize<Dictionary<string,object>>(Console.ReadLine());
        if((string)initialize["method"]!="initialize")Environment.Exit(2);
        Console.WriteLine("not json");
        Console.WriteLine("{\"method\":\"notification\"}");
        Console.WriteLine("{\"id\":99,\"error\":{}}");
        Console.WriteLine("{\"id\":1,\"result\":{}}");Console.Out.Flush();
        var ready=json.Deserialize<Dictionary<string,object>>(Console.ReadLine());
        var read=json.Deserialize<Dictionary<string,object>>(Console.ReadLine());
        if((string)ready["method"]!="initialized" || (string)read["method"]!="account/rateLimits/read")Environment.Exit(3);
        if(mode=="stall") {
            File.WriteAllText(Path.Combine(folder,"fake-quota-pid.txt"),Process.GetCurrentProcess().Id.ToString());
            Thread.Sleep(60000);return;
        }
        if(mode=="reject")Console.WriteLine("{\"id\":2,\"error\":{\"message\":\"test rejection\"}}");
        else Console.WriteLine("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":17,\"windowDurationMins\":300}}}}");
        Console.Out.Flush();
    }
}
