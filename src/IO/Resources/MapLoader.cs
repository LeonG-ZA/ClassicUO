﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using ClassicUO.Utility;

namespace ClassicUO.IO.Resources
{
    class MapLoader : ResourceLoader
    {
        private const int MAPS_COUNT = 6;
        private readonly UOFile[] _filesMap = new UOFile[MAPS_COUNT];
        private readonly UOFileMul[] _filesStatics = new UOFileMul[MAPS_COUNT];
        private readonly UOFileMul[] _filesIdxStatics = new UOFileMul[MAPS_COUNT];

        public IndexMap[][] BlockData { get; } = new IndexMap[MAPS_COUNT][];

        public int[,] MapBlocksSize { get; } = new int[MAPS_COUNT, 2];

        public int[,] MapsDefaultSize { get; } = new int[MAPS_COUNT, 2]
        {
            {
                7168, 4096
            },
            {
                7168, 4096
            },
            {
                2304, 1600
            },
            {
                2560, 2048
            },
            {
                1448, 1448
            },
            {
                1280, 4096
            }
        };

        public override void Load()
        {
            string path;
            bool foundedOneMap = false;

            for (int i = 0; i < MAPS_COUNT; i++)
            {
                path = Path.Combine(FileManager.UoFolderPath, $"map{i}LegacyMUL.uop");

                if (File.Exists(path))
                {
                    _filesMap[i] = new UOFileUop(path, ".dat", loadentries: false);
                    foundedOneMap = true;
                }
                else
                {
                    path = Path.Combine(FileManager.UoFolderPath, $"map{i}.mul");

                    if (File.Exists(path))
                    {
                        _filesMap[i] = new UOFileMul(path, false);
                        foundedOneMap = true;
                    }
                }

                path = Path.Combine(FileManager.UoFolderPath, $"statics{i}.mul");
                if (File.Exists(path)) _filesStatics[i] = new UOFileMul(path, false);
                path = Path.Combine(FileManager.UoFolderPath, $"staidx{i}.mul");
                if (File.Exists(path)) _filesIdxStatics[i] = new UOFileMul(path, false);
            }

            if (!foundedOneMap)
                throw new FileNotFoundException("No maps founded.");
            int mapblocksize = UnsafeMemoryManager.SizeOf<MapBlock>();

            if (_filesMap[0].Length / mapblocksize == 393216 || FileManager.ClientVersion < ClientVersions.CV_4011D)
                MapsDefaultSize[0, 0] = MapsDefaultSize[1, 0] = 6144;

            for (int i = 0; i < MAPS_COUNT; i++)
            {
                MapBlocksSize[i, 0] = MapsDefaultSize[i, 0] >> 3;
                MapBlocksSize[i, 1] = MapsDefaultSize[i, 1] >> 3;
                //LoadMap(i);
            }
        }

        protected override void CleanResources()
        {
            throw new NotImplementedException();
        }


        public unsafe void LoadMap(int i)
        {
            int mapblocksize = UnsafeMemoryManager.SizeOf<MapBlock>();
            int staticidxblocksize = UnsafeMemoryManager.SizeOf<StaidxBlock>();
            int staticblocksize = UnsafeMemoryManager.SizeOf<StaticsBlock>();
            int width = MapBlocksSize[i, 0];
            int height = MapBlocksSize[i, 1];
            int maxblockcount = width * height;
            BlockData[i] = new IndexMap[maxblockcount];
            UOFile file = _filesMap[i];
            UOFile fileidx = _filesIdxStatics[i];
            UOFile staticfile = _filesStatics[i];
            ulong staticidxaddress = (ulong)fileidx.StartAddress;
            ulong endstaticidxaddress = staticidxaddress + (ulong)fileidx.Length;
            ulong staticaddress = (ulong)staticfile.StartAddress;
            ulong endstaticaddress = staticaddress + (ulong)staticfile.Length;
            ulong mapddress = (ulong)file.StartAddress;
            ulong endmapaddress = mapddress + (ulong)file.Length;
            ulong uopoffset = 0;
            int fileNumber = -1;
            bool isuop = file is UOFileUop;

            for (int block = 0; block < maxblockcount; block++)
            {
                ulong realmapaddress = 0, realstaticaddress = 0;
                uint realstaticcount = 0;
                int blocknum = block;

                if (isuop)
                {
                    blocknum &= 4095;
                    int shifted = block >> 12;

                    if (fileNumber != shifted)
                    {
                        fileNumber = shifted;

                        if (shifted < file.Entries.Length)
                            uopoffset = (ulong)file.Entries[shifted].Offset;
                    }
                }

                ulong address = mapddress + uopoffset + (ulong)(blocknum * mapblocksize);

                if (address < endmapaddress)
                    realmapaddress = address;
                ulong stidxaddress = staticidxaddress + (ulong)(block * staticidxblocksize);
                StaidxBlock* bb = (StaidxBlock*)stidxaddress;

                if (stidxaddress < endstaticidxaddress && bb->Size > 0 && bb->Position != 0xFFFFFFFF)
                {
                    ulong address1 = staticaddress + bb->Position;

                    if (address1 < endstaticaddress)
                    {
                        realstaticaddress = address1;
                        realstaticcount = (uint)(bb->Size / staticblocksize);

                        if (realstaticcount > 1024)
                            realstaticcount = 1024;
                    }
                }

                BlockData[i][block] = new IndexMap(realmapaddress, realstaticaddress, realstaticcount, realmapaddress, realstaticaddress, realstaticcount);
            }
        }

        public void UnloadMap(int i)
        {
            if (BlockData[i] != null)
            {
                BlockData[i] = null;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < MAPS_COUNT; i++)
                UnloadMap(i);
        }

        public unsafe RadarMapBlock? GetRadarMapBlock(int map, int blockX, int blockY)
        {
            IndexMap indexMap = GetIndex(map, blockX, blockY);

            if (indexMap.MapAddress == 0)
                return null;
            MapBlock* mp = (MapBlock*)indexMap.MapAddress;
            MapCells* cells = (MapCells*)&mp->Cells;

            RadarMapBlock mb = new RadarMapBlock
            {
                Cells = new RadarMapcells[8, 8]
            };

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    ref MapCells cell = ref cells[y * 8 + x];
                    ref RadarMapcells outcell = ref mb.Cells[x, y];
                    outcell.Graphic = cell.TileID;
                    outcell.Z = cell.Z;
                    outcell.IsLand = true;
                }
            }

            StaticsBlock* sb = (StaticsBlock*)indexMap.StaticAddress;

            if (sb != null)
            {
                int count = (int)indexMap.StaticCount;

                for (int c = 0; c < count; c++)
                {
                    if (sb->Color > 0 && sb->Color != 0xFFFF && !GameObjectHelper.IsNoDrawable(sb->Color))
                    {
                        ref RadarMapcells outcell = ref mb.Cells[sb->X, sb->Y];

                        if (outcell.Z <= sb->Z)
                        {
                            outcell.Graphic = sb->Color;
                            outcell.Z = sb->Z;
                            outcell.IsLand = false;
                        }
                    }

                    sb++;
                }
            }

            return mb;
        }

        public IndexMap GetIndex(int map, int x, int y)
        {
            int block = x * MapBlocksSize[map, 1] + y;

            return BlockData[map][block];
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct StaticsBlock
    {
        public readonly ushort Color;
        public readonly byte X;
        public readonly byte Y;
        public readonly sbyte Z;
        public readonly ushort Hue;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct StaidxBlock
    {
        public readonly uint Position;
        public readonly uint Size;
        public readonly uint Unknown;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapCells
    {
        public ushort TileID;
        public sbyte Z;
    }

    //[StructLayout(LayoutKind.Sequential, Pack = 1)]
    //public struct MapBlock
    //{
    //    public readonly uint Header;
    //    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    //    public MapCells[] Cells;
    //}

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4 + 64 * 3)]
    public struct MapBlock
    {
        public readonly uint Header;
        public unsafe MapCells* Cells;
    }

    //[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4 + 64 * 3)]
    //public struct MapBlock2
    //{
    //    public readonly uint Header;
    //    public IntPtr Cells;
    //}

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RadarMapcells
    {
        public ushort Graphic;
        public sbyte Z;
        public bool IsLand;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RadarMapBlock
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public RadarMapcells[,] Cells;
    }

    public readonly struct IndexMap
    {
        public IndexMap(ulong mapAddress, ulong staticAddress, uint staticCount, ulong originalMapAddress, ulong originalStaticAddress, uint originalStaticCount)
        {
            MapAddress = mapAddress;
            StaticAddress = staticAddress;
            StaticCount = staticCount;
            OriginalMapAddress = originalMapAddress;
            OriginalStaticAddress = originalStaticAddress;
            OriginalStaticCount = originalStaticCount;
        }

        public readonly ulong MapAddress;
        public readonly ulong OriginalMapAddress;
        public readonly ulong OriginalStaticAddress;
        public readonly uint OriginalStaticCount;
        public readonly ulong StaticAddress;
        public readonly uint StaticCount;
        public static readonly IndexMap Invalid = new IndexMap();
    }
}