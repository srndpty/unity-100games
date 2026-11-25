#nullable enable
using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class MapAsset : ScriptableObject
    {
        public string Id = string.Empty;
        public string MapName = string.Empty;
        public bool IsSafeZone;
        public string EncounterTableId = string.Empty;
        public TextAsset? GridFile;
        public PortalDef[] Portals = Array.Empty<PortalDef>();
        public ChestDef[] Chests = Array.Empty<ChestDef>();
        public LockedDoorDef[] Doors = Array.Empty<LockedDoorDef>();
        public NpcDef[] Npcs = Array.Empty<NpcDef>();
        public BossTriggerDef Boss = new();

        public MapDef ToMapDef() => new()
        {
            Id = Id ?? string.Empty,
            Name = MapName ?? string.Empty,
            IsSafeZone = IsSafeZone,
            EncounterTableId = EncounterTableId ?? string.Empty,
            GridText = GridFile != null ? GridFile.text : string.Empty,
            Portals = Portals == null ? Array.Empty<PortalDef>() : Array.ConvertAll(Portals, p => p == null ? new PortalDef() : p.Copy()),
            Chests = Chests == null ? Array.Empty<ChestDef>() : Array.ConvertAll(Chests, c => c == null ? new ChestDef() : c.Copy()),
            Doors = Doors == null ? Array.Empty<LockedDoorDef>() : Array.ConvertAll(Doors, d => d == null ? new LockedDoorDef() : d.Copy()),
            Npcs = Npcs == null ? Array.Empty<NpcDef>() : Array.ConvertAll(Npcs, n => n == null ? new NpcDef() : n.Copy()),
            Boss = Boss == null ? new BossTriggerDef() : Boss.Copy(),
        };
    }
}
