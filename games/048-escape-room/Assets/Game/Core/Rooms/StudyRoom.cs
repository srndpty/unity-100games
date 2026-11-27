using Game.Core.Geometry;
using Game.Core.Mechanisms;

namespace Game.Core.Rooms
{
    /// <summary>
    /// 本番の部屋：古い天文学者の書斎（4 m 四方・天井 2.8 m、原点は床の中央）。
    /// 北：机（引き出しのダイヤル錠・真鍮の筒）と窓辺の望遠鏡。東：キャビネット（鍵穴）と本棚（天文書）。
    /// 南：出口の扉と柱時計。西：天球儀（リング）と金庫（宝石の順番）。
    /// 中に物が入る家具は、物の入る空洞を本体の箱で囲み、開口を前板・扉の箱でふさぐ（閉じている間は前板に当たる）。
    /// </summary>
    public static class StudyRoom
    {
        public const string DeskView = "desk";

        public static RoomDef Create()
        {
            var b = new RoomBuilder("天文学者の書斎", B(-2000, 0, -2000, 2000, 2800, 2000));

            // 持ち物と手に取る物（仕掛けの番号は下の順に固定する）。
            const int dial = 0;
            const int cabinet = 1;
            const int globe = 2;
            const int clock = 3;
            const int safe = 4;
            const int door = 5;
            var cylinder = b.Examinable("cylinder", "真鍮の筒", new Vec3i(0, -1, 0), dial);
            var discFace = b.Examinable("disc", "星図の円盤", new Vec3i(0, 0, 1), globe);
            var book = b.Examinable("book", "古い天文書", new Vec3i(0, 0, 1), clock);
            var brassKey = b.Item("brass_key", "真鍮の鍵", "開いた引き出しの中");
            var disc = b.Item("star_disc", "星図の円盤", "開いたキャビネットの中", discFace);
            var gear = b.Item("gear", "歯車", "開いた天球儀のふたの下");
            var handle = b.Item("handle", "金庫のハンドル", "開いた柱時計の振り子の扉の中");
            var exitKey = b.Item("exit_key", "出口の鍵", "開いた金庫の中");

            b.Mechanism("dial", "引き出しのダイヤル錠", MechanismKind.Dial, -1,
                "机の引き出しに 3 桁のダイヤル錠がある。数字はどこかに刻まれていそうだ。",
                "机の上の真鍮の筒を手に取って、底を見てみよう。");
            b.Mechanism("cabinet", "キャビネット", MechanismKind.KeyLock, brassKey,
                "キャビネットには鍵がかかっている。",
                "真鍮の鍵が合いそうだ。");
            b.Mechanism("globe", "天球儀", MechanismKind.Rings, disc,
                "天球儀の 3 本のリングは、星座に合わせて回るようだ。",
                "星図の円盤を手に取って裏返すと、3 つの星座が上から並んでいる。");
            b.Mechanism("clock", "柱時計", MechanismKind.Clock, gear,
                "柱時計の針を、ある時刻に合わせると何かが起きそうだ。",
                "本棚から飛び出している天文書を手に取って、裏表紙を見てみよう。");
            b.Mechanism("safe", "金庫", MechanismKind.Sequence, handle,
                "金庫の 4 つの宝石には、押す順番があるようだ。",
                "望遠鏡をのぞくと、窓の外の星が宝石の色の順に瞬く。");
            b.Mechanism("door", "出口の扉", MechanismKind.KeyLock, exitKey,
                "出口の扉には鍵がかかっている。",
                "出口の鍵が合いそうだ。");

            // 視点：壁は部屋の中央の目の高さから、近景は仕掛けの手前から。
            var center = new Vec3i(0, 1600, 0);
            b.WallView("north", "北の壁", new CameraPose(center, 0, 1, 1500));
            b.WallView("east", "東の壁", new CameraPose(center, 1, 1, 1500));
            b.WallView("south", "南の壁", new CameraPose(center, 2, 1, 1500));
            b.WallView("west", "西の壁", new CameraPose(center, 3, 1, 1500));
            var deskView = b.CloseUp(DeskView, "机", new CameraPose(new Vec3i(-900, 1150, 700), 0, 3, 1500));
            var scopeView = b.CloseUp("telescope", "望遠鏡", new CameraPose(new Vec3i(850, 1550, 1300), 0, 0, 2400), safe);
            var cabinetView = b.CloseUp("cabinet", "キャビネット", new CameraPose(new Vec3i(800, 1400, 1050), 1, 2, 1500));
            var shelfView = b.CloseUp("bookshelf", "本棚", new CameraPose(new Vec3i(900, 1300, -1000), 1, 0, 1500));
            var doorView = b.CloseUp("door", "出口の扉", new CameraPose(new Vec3i(700, 1300, -900), 2, 1, 1500));
            var clockView = b.CloseUp("clock", "柱時計", new CameraPose(new Vec3i(-450, 1500, -300), 2, 1, 1300));
            var globeView = b.CloseUp("globe", "天球儀", new CameraPose(new Vec3i(-600, 1550, -900), 3, 3, 1500));
            var safeView = b.CloseUp("safe", "金庫", new CameraPose(new Vec3i(-800, 1100, 900), 3, 4, 1500));

            // 北：机（空洞 x -1150〜-650・y 540〜700・z 1450〜1900 を本体で囲む）。
            b.Body("desk.top", "机の天板", deskView, B(-1500, 700, 1450, -300, 760, 2000));
            b.Body("desk.left", "机の脚", deskView, B(-1500, 0, 1450, -1150, 700, 2000));
            b.Body("desk.right", "机の脚", deskView, B(-650, 0, 1450, -300, 700, 2000));
            b.Body("desk.under", "机の下", deskView, B(-1150, 0, 1450, -650, 540, 2000));
            b.Body("desk.back", "引き出しの奥", deskView, B(-1150, 540, 1900, -650, 700, 2000));
            b.Pickup("desk.key", "真鍮の鍵", deskView, B(-940, 540, 1470, -840, 560, 1530), brassKey, dial);
            b.Body("desk.front", "引き出しの前板", deskView, B(-1150, 540, 1430, -650, 700, 1450), Presence.WhileClosed, dial);
            b.Body("desk.flap", "倒れた前板", deskView, B(-1150, 500, 1290, -650, 520, 1430), Presence.WhileOpen, dial);
            for (int i = 0; i < 3; i++)
            {
                var x = -610 + (i * 90);
                b.Slot($"desk.dial{i}", $"ダイヤルの {i + 1} 桁目", deskView, B(x, 420, 1420, x + 80, 520, 1450), dial, i);
            }

            b.Examine("desk.cylinder", "真鍮の筒", deskView, B(-500, 760, 1600, -420, 900, 1680), cylinder);
            b.Body("window", "窓", scopeView, B(300, 1000, 1980, 1500, 2200, 2000));
            b.Body("telescope", "望遠鏡", scopeView, B(750, 900, 1450, 950, 1350, 1650));

            // 東：キャビネット（空洞 x 1600〜1980・y 800〜1500・z 550〜1350）と本棚。
            b.Body("cabinet.base", "キャビネットの下段", cabinetView, B(1600, 0, 500, 2000, 800, 1400));
            b.Body("cabinet.top", "キャビネットの上段", cabinetView, B(1600, 1500, 500, 2000, 1800, 1400));
            b.Body("cabinet.side0", "キャビネットの側板", cabinetView, B(1600, 800, 500, 2000, 1500, 550));
            b.Body("cabinet.side1", "キャビネットの側板", cabinetView, B(1600, 800, 1350, 2000, 1500, 1400));
            b.Body("cabinet.back", "キャビネットの奥", cabinetView, B(1980, 800, 550, 2000, 1500, 1350));
            b.Pickup("cabinet.disc", "星図の円盤", cabinetView, B(1700, 800, 870, 1880, 830, 1030), disc, cabinet);
            b.Body("cabinet.door", "キャビネットの扉", cabinetView, B(1580, 800, 550, 1600, 1500, 1350), Presence.WhileClosed, cabinet);
            b.Body("cabinet.open", "開いた扉", cabinetView, B(780, 800, 530, 1580, 1500, 550), Presence.WhileOpen, cabinet);
            b.Keyhole("cabinet.keyhole", "鍵穴", cabinetView, B(1580, 700, 920, 1600, 780, 980), cabinet);
            b.Body("shelf", "本棚", shelfView, B(1700, 0, -1500, 2000, 2000, -500));
            for (int i = 0; i < 6; i++)
            {
                var z = -1440 + (i * 70);
                if (z > -1100) { z += 260; }
                b.Body($"shelf.book{i}", "本", shelfView, B(1660, 1080, z, 1700, 1320, z + 60));
            }

            b.Examine("shelf.astronomy", "古い天文書", shelfView, B(1620, 1050, -1030, 1700, 1340, -970), book);

            // 南：出口の扉と柱時計（空洞 x -600〜-300・y 300〜1300・z -1950〜-1650）。
            b.Body("door.slab", "出口の扉", doorView, B(200, 0, -2000, 1200, 2100, -1960));
            b.Keyhole("door.keyhole", "鍵穴", doorView, B(1060, 950, -1960, 1120, 1050, -1940), door);
            b.Body("clock.lower", "柱時計の台", clockView, B(-700, 0, -2000, -200, 300, -1650));
            b.Body("clock.upper", "柱時計の文字盤", clockView, B(-700, 1300, -2000, -200, 2100, -1650));
            b.Body("clock.side0", "柱時計の側板", clockView, B(-700, 300, -2000, -600, 1300, -1650));
            b.Body("clock.side1", "柱時計の側板", clockView, B(-300, 300, -2000, -200, 1300, -1650));
            b.Body("clock.back", "柱時計の奥", clockView, B(-600, 300, -2000, -300, 1300, -1950));
            b.Pickup("clock.handle", "金庫のハンドル", clockView, B(-500, 300, -1850, -400, 340, -1750), handle, clock);
            b.Body("clock.door", "振り子の扉", clockView, B(-600, 300, -1650, -300, 1300, -1630), Presence.WhileClosed, clock);
            b.Body("clock.open", "開いた振り子の扉", clockView, B(-300, 300, -1650, -280, 1300, -1050), Presence.WhileOpen, clock);
            b.Socket("clock.axle", "文字盤の軸", clockView, B(-490, 1660, -1650, -410, 1740, -1630), clock);
            b.Slot("clock.hour", "短針のつまみ", clockView, B(-420, 1340, -1650, -340, 1420, -1630), clock, 0);
            b.Slot("clock.minute", "長針のつまみ", clockView, B(-560, 1340, -1650, -480, 1420, -1630), clock, 1);

            // 西：天球儀（台座の前にくぼみ・3 本のリング・ふたの下に歯車）と金庫（空洞 x -1950〜-1550・y 100〜500・z 600〜1200）。
            b.Body("globe.stand", "天球儀の台座", globeView, B(-1700, 0, -1100, -1300, 800, -700));
            b.Body("globe.base", "天球儀の受け皿", globeView, B(-1680, 800, -1080, -1320, 900, -720));
            for (int i = 0; i < 3; i++)
            {
                var y = 1100 - (i * 100);
                b.Slot($"globe.ring{i}", i == 0 ? "上のリング" : i == 1 ? "中のリング" : "下のリング", globeView, B(-1650, y, -1050, -1350, y + 100, -750), globe, i);
            }

            b.Pickup("globe.gear", "歯車", globeView, B(-1540, 1200, -940, -1460, 1240, -860), gear, globe);
            b.Body("globe.lid", "天球儀のふた", globeView, B(-1660, 1200, -1060, -1340, 1280, -740), Presence.WhileClosed, globe);
            b.Body("globe.lidOpen", "外したふた", globeView, B(-1660, 0, -1330, -1340, 80, -1170), Presence.WhileOpen, globe);
            b.Socket("globe.socket", "台座のくぼみ", globeView, B(-1300, 500, -1000, -1280, 700, -800), globe);
            b.Examine("globe.disc", "はめた星図の円盤", globeView, B(-1280, 500, -1000, -1260, 700, -800), discFace, Presence.WhileInstalled, globe);

            b.Body("safe.bottom", "金庫の底", safeView, B(-2000, 0, 500, -1550, 100, 1300));
            b.Body("safe.top", "金庫の上部", safeView, B(-2000, 500, 500, -1550, 700, 1300));
            b.Body("safe.side0", "金庫の側面", safeView, B(-2000, 100, 500, -1550, 500, 600));
            b.Body("safe.side1", "金庫の側面", safeView, B(-2000, 100, 1200, -1550, 500, 1300));
            b.Body("safe.back", "金庫の奥", safeView, B(-2000, 100, 600, -1950, 500, 1200));
            b.Pickup("safe.key", "出口の鍵", safeView, B(-1800, 100, 860, -1700, 120, 940), exitKey, safe);
            b.Body("safe.door", "金庫の扉", safeView, B(-1550, 100, 600, -1530, 500, 1200), Presence.WhileClosed, safe);
            b.Body("safe.open", "開いた金庫の扉", safeView, B(-1530, 100, 580, -930, 500, 600), Presence.WhileOpen, safe);
            b.Socket("safe.socket", "ハンドルの穴", safeView, B(-1550, 250, 1210, -1530, 350, 1290), safe);
            for (int i = 0; i < 4; i++)
            {
                var z = 650 + (i * 140);
                b.Slot($"safe.gem{i}", SequenceLock.Gems[i], safeView, B(-1550, 560, z, -1530, 640, z + 80), safe, i);
            }

            return b.Build(door);
        }

        private static Box3 B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Vec3i(x0, y0, z0), new Vec3i(x1, y1, z1));
    }
}
