using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("CarbonAliasesDownloader", "ThePitereq", "1.2.0")]
    public class CarbonAliasesDownloader : RustPlugin
    {
        private void OnServerInitialized()
        {
#if CARBON
            Debug.Log("[CarbonAliases Logger] This server is running on Carbon. Extension is not required! Unloading...");
            Server.Command($"c.unload {Name}");
            return;
#endif
            if (Interface.Oxide.GetExtension("CarbonAliases") == null)
                ServerMgr.Instance.StartCoroutine(DownloadExtension());
            else
                Debug.Log("[CarbonAliases Logger] Extension already found. If you want to try to upgrade it, run UpdateCarbonAliasesExt command.");
        }

        private static IEnumerator DownloadExtension(bool loadExt = true)
        {
            Debug.Log("[CarbonAliases Logger] Starting downloading extension.");
            UnityWebRequest www = UnityWebRequest.Get("https://www.dropbox.com/scl/fi/wmjj53lg06h57bvtew1ro/Oxide.Ext.CarbonAliases.dll?rlkey=7g9f2ni5fbw5auoyzpi1vkxc5&dl=1");
            www.downloadHandler = new DownloadHandlerFile(Application.platform == RuntimePlatform.WindowsPlayer ? $@"{Interface.Oxide.ExtensionDirectory}\Oxide.Ext.CarbonAliases.dll" : $@"{Interface.Oxide.ExtensionDirectory}/Oxide.Ext.CarbonAliases.dll");
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[CarbonAliases Logger] Error during installing CarbonAliases Extension: {www.error}");
                www.Dispose();
                yield break;
            }
            yield return new WaitUntil(() => (www.downloadHandler as DownloadHandlerFile).isDone);
            if (loadExt)
            {
                Interface.Oxide.LoadExtension("Oxide.Ext.CarbonAliases");
                Debug.Log("[CarbonAliases Logger] Extension updated and loaded successfully! Reload your plugins that require CarbonAliases extension!");
            }
            else
                Debug.Log("[CarbonAliases Logger] Extension downloaded successfully! Restart your server to update your CarbonAliases version.");
            www.Dispose();
        }


        [ConsoleCommand("UpdateCarbonAliasesExt")]
        private void UpdateCarbonAliasesCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                SendReply(arg, "You are not authorized to run this command!");
                return;
            }
            ServerMgr.Instance.StartCoroutine(DownloadExtension(false));
        }
    }
}
