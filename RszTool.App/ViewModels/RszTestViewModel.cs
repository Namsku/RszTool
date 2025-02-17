﻿using System.ComponentModel;
using RszTool.App.Common;

namespace RszTool.App.ViewModels
{
    public class RszTestViewModel : INotifyPropertyChanged
    {
        public RszTestViewModel()
        {
            InstanceList = InstanceTestData.GetItems().ToList();
        }

        public List<RszInstance> InstanceList { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public RelayCommand CopyInstance => new(OnCopyInstance);
        public RelayCommand CopyArrayItem => new(OnCopyArrayItem);

        private static void OnCopyInstance(object arg)
        {
            if (arg is RszInstance instance)
            {
                // Console.WriteLine(instance.Stringify());
            }
        }

        private static void OnCopyArrayItem(object arg)
        {
            if (arg is RszFieldArrayInstanceItemViewModel item)
            {
                // Console.WriteLine(item.Instance.Stringify());
            }
        }
    }


    public class InstanceTestData
    {
        public static IEnumerable<RszInstance> GetItems()
        {
            RszClass AccessoryEffectSettingUserdata = new()
            {
                crc = 1583866028,
                typeId = 0x97D127B9,
                name = "chainsaw.AccessoryEffectSettingUserdata",
                fields = [
                    new()
                    {
                        name = "_Settings",
                        align = 4,
                        array = true,
                        type = RszFieldType.Object,
                        original_type = "chainsaw.AccessoryEffectSingleSettingData"
                    }
                ]
            };
            RszClass AccessoryEffectSingleSettingData = new()
            {
                crc = 1583866028,
                typeId = 0x97D127B9,
                name = "chainsaw.AccessoryEffectSingleSettingData",
                fields = [
                    new()
                    {
                        name = "_AccessoryId",
                        align = 4,
                        type = RszFieldType.Sphere,
                        original_type = "Sphere"
                    },
                    new()
                    {
                        name = "_Effects",
                        align = 4,
                        array = true,
                        type = RszFieldType.Object,
                        original_type = "chainsaw.StatusEffectSetting"
                    },
                ]
            };
            RszClass StatusEffectSetting = new()
            {
                crc = 1583866028,
                typeId = 0x97D127B9,
                name = "chainsaw.StatusEffectSetting",
                fields = [
                    new()
                    {
                        name = "_StatusEffectID",
                        align = 4,
                        type = RszFieldType.U32,
                        original_type = "System.Int"
                    },
                    new()
                    {
                        name = "_Value",
                        align = 4,
                        type = RszFieldType.F32,
                        original_type = "System.Single"
                    },
                ]
            };
            List<RszInstance> instances = new()
            {
                new(AccessoryEffectSettingUserdata, 3, values: [
                    new List<object>
                    {
                        new RszInstance(AccessoryEffectSingleSettingData, 2, values: [
                            new via.Sphere(),
                            new List<object>
                            {
                                new RszInstance(StatusEffectSetting, 1, values: [1000000u, 30.0f])
                            }
                        ])
                    }
                ]),
            };

            return instances;
        }
    }
}
