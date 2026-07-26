using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(ClipDwg.Plugin))]
[assembly: CommandClass(typeof(ClipDwg.Commands))]

namespace ClipDwg;

/// <summary>NETLOAD / 자동 로드 시 AutoCAD가 호출하는 진입점.</summary>
public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        doc?.Editor.WriteMessage("\nclipdwg 로드됨. CLIPDWG = 복사, CLIPDWGCFG = 옵션\n");
    }

    public void Terminate()
    {
    }
}
