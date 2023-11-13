using System.Text;

namespace RszTool
{
    public class RSZFile : BaseRszFile
    {
        public struct HeaderStruct
        {
            public uint magic;
            public uint version;
            public int objectCount;
            public int instanceCount;
            public long userdataCount;
            public long instanceOffset;
            public long dataOffset;
            public long userdataOffset;
        }

        public struct ObjectTable
        {
            public int instanceId;
        }

        public StructModel<HeaderStruct> Header { get; } = new();
        public List<StructModel<ObjectTable>> ObjectTableList { get; } = new();
        public List<InstanceInfo> InstanceInfoList { get; } = new();
        public List<IRSZUserDataInfo> RSZUserDataInfoList { get; } = new();

        public List<RszInstance> InstanceList { get; } = new();
        public List<RSZFile>? EmbeddedRSZFileList { get; private set; }

        public RSZFile(RszFileOption option, FileHandler fileHandler) : base(option, fileHandler)
        {
        }

        public const uint Magic = 0x5a5352;

        protected override bool DoRead()
        {
            var handler = FileHandler;
            handler.Seek(0);
            if (!Header.Read(handler)) return false;
            if (Header.Data.magic != Magic)
            {
                throw new InvalidDataException($"Not a RSZ data");
            }

            var rszParser = RszParser;

            ObjectTableList.Read(handler, Header.Data.objectCount);

            handler.Seek(Header.Data.instanceOffset);
            for (int i = 0; i < Header.Data.instanceCount; i++)
            {
                InstanceInfo instanceInfo = new();
                instanceInfo.Read(handler);
                instanceInfo.ReadClassName(rszParser);
                InstanceInfoList.Add(instanceInfo);
            }

            handler.Seek(Header.Data.userdataOffset);
            Dictionary<int, int> instanceIdToUserData = new();
            if (Option.TdbVersion < 67)
            {
                if (Header.Data.userdataCount > 0)
                {
                    EmbeddedRSZFileList = new();
                    for (int i = 0; i < (int)Header.Data.userdataCount; i++)
                    {
                        RSZUserDataInfo_TDB_LE_67 rszUserDataInfo = new();
                        rszUserDataInfo.Read(handler);
                        rszUserDataInfo.ReadClassName(rszParser);
                        RSZUserDataInfoList.Add(rszUserDataInfo);
                        instanceIdToUserData[rszUserDataInfo.instanceId] = i;

                        RSZFile embeddedRSZFile = new(Option, handler.WithOffset(rszUserDataInfo.RSZOffset));
                        embeddedRSZFile.Read();
                        EmbeddedRSZFileList.Add(embeddedRSZFile);
                    }
                }
            }
            else
            {
                for (int i = 0; i < (int)Header.Data.userdataCount; i++)
                {
                    RSZUserDataInfo rszUserDataInfo = new();
                    rszUserDataInfo.Read(handler);
                    RSZUserDataInfoList.Add(rszUserDataInfo);
                    instanceIdToUserData[rszUserDataInfo.instanceId] = i;
                }
            }

            // read instance data
            handler.Seek(Header.Data.dataOffset);
            for (int i = 0; i < InstanceInfoList.Count; i++)
            {
                RszClass? rszClass = RszParser.GetRSZClass(InstanceInfoList[i].typeId);
                if (rszClass == null)
                {
                    // throw new InvalidDataException($"RszClass {InstanceInfoList[i].typeId} not found!");
                    Console.Error.WriteLine($"RszClass {InstanceInfoList[i].typeId} not found!");
                    continue;
                }
                if (!instanceIdToUserData.TryGetValue(i, out int userDataIdx))
                {
                    userDataIdx = -1;
                }
                RszInstance instance = new(rszClass, i, userDataIdx);
                if (userDataIdx != -1)
                {
                    instance.RSZUserData = RSZUserDataInfoList[userDataIdx];
                }
                else
                {
                    instance.Read(handler);
                }
                InstanceList.Add(instance);
            }
            return true;
        }

        protected override bool DoWrite()
        {
            var handler = FileHandler;

            handler.Seek(Header.Size);

            handler.Align(16);
            ObjectTableList.Write(handler);

            handler.Align(16);
            Header.Data.instanceOffset = handler.Tell();
            InstanceInfoList.Write(handler);

            handler.Align(16);
            Header.Data.userdataOffset = handler.Tell();
            RSZUserDataInfoList.Write(handler);

            handler.Align(16);
            handler.StringTableFlush();

            // embedded userdata
            if (Option.TdbVersion <= 67 && EmbeddedRSZFileList != null)
            {
                for (int i = 0; i < RSZUserDataInfoList.Count; i++)
                {
                    handler.Align(16);
                    var item = (RSZUserDataInfo_TDB_LE_67)RSZUserDataInfoList[i];
                    item.RSZOffset = handler.Tell();
                    var embeddedRSZ = EmbeddedRSZFileList[i];
                    embeddedRSZ.WriteTo(FileHandler.WithOffset(item.RSZOffset));
                    // rewrite
                    item.dataSize = (uint)embeddedRSZ.Size;
                    item.Rewrite(handler);
                }
            }

            handler.Align(16);
            Header.Data.dataOffset = handler.Tell();
            InstanceList.Write(handler);

            Header.Data.objectCount = ObjectTableList.Count;
            Header.Data.instanceCount = InstanceList.Count;
            Header.Data.userdataCount = RSZUserDataInfoList.Count;
            Header.Rewrite(handler);
            return true;
        }

        public RszInstance GetGameObject(int objectIndex)
        {
            return InstanceList[ObjectTableList[objectIndex].Data.instanceId];
        }

        public string InstanceStringify(RszInstance instance)
        {
            StringBuilder sb = new();
            sb.AppendLine(instance.Stringify(InstanceList));
            return sb.ToString();
        }

        public string ObjectsStringify()
        {
            StringBuilder sb = new();
            foreach (var item in ObjectTableList)
            {
                RszInstance instance = InstanceList[item.Data.instanceId];
                sb.AppendLine(instance.Stringify(InstanceList));
            }
            return sb.ToString();
        }

        public RszInstance? CreateInstance(string className, int index = -1)
        {
            RszClass? rszClass = RszParser.GetRSZClass(className);
            if (index == -1)
            {
                index = InstanceList.Count;
            }
            if (rszClass == null) return null;
            RszInstance instance = new(rszClass, index)
            {
                Start = Start + Size
            };
            // TODO Values
            return instance;
        }

        public void CheckInstanceIndex(RszInstance instance)
        {
            if (instance.Index == -1 || instance.Index >= InstanceList.Count || InstanceList[instance.Index] != instance)
            {
                instance.Index = InstanceList.IndexOf(instance);
                if (instance.Index == -1)
                {
                    instance.Index = InstanceList.Count;
                    InstanceList.Add(instance);
                }
            }
        }

        /// <summary>
        /// 实例的字段值，如果是对象序号，替换成对应的实例对象
        /// </summary>
        /// <param name="instance"></param>
        public void InstanceUnflatten(RszInstance instance, bool recursive = true)
        {
            if (instance.RSZUserData != null) return;
            for (int i = 0; i < instance.RszClass.fields.Length; i++)
            {
                var field = instance.RszClass.fields[i];
                if (field.type == RszFieldType.Object)
                {
                    if (field.array)
                    {
                        var items = (List<object>)instance.Values[i];
                        for (int j = 0; j < items.Count; j++)
                        {
                            if (items[j] is int objectId)
                            {
                                items[j] = InstanceList[objectId];
                                if (recursive)
                                {
                                    InstanceUnflatten(InstanceList[objectId], recursive);
                                }
                            }
                        }
                    }
                    else if (instance.Values[i] is int objectId)
                    {
                        instance.Values[i] = InstanceList[objectId];
                        if (recursive)
                        {
                            InstanceUnflatten(InstanceList[objectId], recursive);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 所有实例的字段值，如果是对象序号，替换成对应的实例对象
        /// </summary>
        public void InstanceListUnflatten()
        {
            foreach (var instance in InstanceList)
            {
                InstanceUnflatten(instance);
            }
        }

        /// <summary>
        /// 实例的字段值，如果是对象，替换成实例序号
        /// </summary>
        /// <param name="instance"></param>
        public void InstanceFlatten(RszInstance instance, bool recursive = true)
        {
            if (instance.RSZUserData == null)
            {
                for (int i = 0; i < instance.RszClass.fields.Length; i++)
                {
                    var field = instance.RszClass.fields[i];
                    if (field.type == RszFieldType.Object)
                    {
                        if (field.array)
                        {
                            var items = (List<object>)instance.Values[i];
                            for (int j = 0; j < items.Count; j++)
                            {
                                if (items[j] is RszInstance instanceValue)
                                {
                                    if (recursive)
                                    {
                                        InstanceFlatten(instanceValue, recursive);
                                    }
                                    CheckInstanceIndex(instanceValue);
                                    items[j] = instanceValue.Index;
                                }
                            }
                        }
                        else if (instance.Values[i] is RszInstance instanceValue)
                        {
                            if (recursive)
                            {
                                InstanceFlatten(instanceValue, recursive);
                            }
                            CheckInstanceIndex(instanceValue);
                            instance.Values[i] = instanceValue.Index;
                        }
                    }
                }
            }
            CheckInstanceIndex(instance);
            if (instance.RSZUserData != null && instance.RSZUserData.InstanceId != instance.Index)
            {
                instance.RSZUserData.InstanceId = instance.Index;
            }
        }

        /// <summary>
        /// 所有实例的字段值，如果是对象，替换成实例序号
        /// </summary>
        public void InstanceListFlatten()
        {
            foreach (var instance in InstanceList)
            {
                InstanceFlatten(instance);
            }
        }

        /// <summary>
        /// 根据实例列表，重建InstanceInfo
        /// </summary>
        /// <param name="flatten">是否先进行flatten</param>
        public void RebulidInstanceInfo(bool flatten = true)
        {
            if (flatten)
            {
                InstanceListFlatten();
            }
            InstanceInfoList.Clear();
            RSZUserDataInfoList.Clear();
            foreach (var instance in InstanceList)
            {
                InstanceInfoList.Add(new InstanceInfo
                {
                    typeId = instance.RszClass.typeId,
                    CRC = instance.RszClass.crc
                });
                if (instance.RSZUserData != null)
                {
                    RSZUserDataInfoList.Add(instance.RSZUserData);
                }
            }
        }
    }


    public class InstanceInfo : BaseModel
    {
        public uint typeId;
        public uint CRC;
        public string? ClassName { get; set; }

        protected override bool DoRead(FileHandler handler)
        {
            handler.Read(ref typeId);
            handler.Read(ref CRC);
            return true;
        }

        protected override bool DoWrite(FileHandler handler)
        {
            handler.Write(typeId);
            handler.Write(CRC);
            return true;
        }

        public void ReadClassName(RszParser parser)
        {
            ClassName = parser.GetRSZClassName(typeId);
        }
    }

    public interface IRSZUserDataInfo : IModel
    {
        int InstanceId { get; set; }
        uint TypeId { get; }
        string? ClassName { get; }
    }

    public class RSZUserDataInfo : BaseModel, IRSZUserDataInfo
    {
        public int instanceId;
        public uint typeId;
        public ulong pathOffset;
        public string? path;

        public int InstanceId { get => instanceId; set => instanceId = value; }
        public uint TypeId => typeId;
        public string? ClassName { get; set; }

        protected override bool DoRead(FileHandler handler)
        {
            handler.Read(ref instanceId);
            handler.Read(ref typeId);
            handler.Read(ref pathOffset);
            path = handler.ReadWString((long)pathOffset);
            return true;
        }

        protected override bool DoWrite(FileHandler handler)
        {
            handler.Write(instanceId);
            handler.Write(typeId);
            handler.StringTableAdd(path);
            handler.Write(pathOffset);
            return true;
        }

        public void ReadClassName(RszParser parser)
        {
            ClassName = parser.GetRSZClassName(typeId);
        }
    }

    public class RSZUserDataInfo_TDB_LE_67 : BaseModel, IRSZUserDataInfo
    {
        public int instanceId;
        public uint typeId;
        public uint jsonPathHash;
        public uint dataSize;
        public long RSZOffset;

        public int InstanceId { get => instanceId; set => instanceId = value; }
        public uint TypeId => typeId;
        public string? ClassName { get; set; }

        protected override bool DoRead(FileHandler handler)
        {
            handler.Read(ref instanceId);
            handler.Read(ref typeId);
            handler.Read(ref jsonPathHash);
            handler.Read(ref dataSize);
            handler.Read(ref RSZOffset);
            return true;
        }

        protected override bool DoWrite(FileHandler handler)
        {
            handler.Write(instanceId);
            handler.Write(typeId);
            handler.Write(jsonPathHash);
            handler.Write(dataSize);
            handler.Write(RSZOffset);
            return true;
        }

        public void ReadClassName(RszParser parser)
        {
            ClassName = parser.GetRSZClassName(typeId);
        }
    }
}
