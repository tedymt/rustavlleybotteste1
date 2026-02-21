using ProtoBuf;
using System.Collections.Generic;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("TPMarker", "MikeHawke", "1.0.0")]
    [Description("Teleports player to map marker")]
    public class TPMarker : RustPlugin 
    {
        public List<ulong> users = new List<ulong>();
        private void OnServerInitialized()
        { permission.RegisterPermission("TPMarker.use", this); }
        [ChatCommand("tpm")]
        void tpm(BasePlayer player)
        {
            if (player.IsAdmin || permission.UserHasPermission(player.userID.ToString(), "TPMarker.use"))
                if (!users.Contains(player.userID)) { users.Add(player.userID); SendReply(player, "TPMarker Enabled"); return; }
            if (users.Contains(player.userID)) { users.Remove(player.userID); SendReply(player, "TPMarker Disabled"); return; }
        }
        private void TP(BasePlayer player, MapNote note)
        {  player.flyhackPauseTime = 10f; player.Teleport(note.worldPosition + new Vector3(0, TerrainMeta.HeightMap.GetHeight(note.worldPosition), 0)); }
        private void OnMapMarkerAdded(BasePlayer player, MapNote note)
        {
            if (player == null || note == null || player.isMounted || !player.IsAlive() || !users.Contains(player.userID)) return;
            TP(player, note); 
        }
    }
}