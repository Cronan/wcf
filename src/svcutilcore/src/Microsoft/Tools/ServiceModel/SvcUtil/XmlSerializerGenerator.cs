// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Tools.ServiceModel.SvcUtil
{
    using System;
    using System.ServiceModel.Description;
    using System.Reflection;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Globalization;
    using System.Text;
    using System.Xml.Serialization;
    using System.CodeDom.Compiler;
    using System.ServiceModel;

    internal class XmlSerializerGenerator : OutputModule
    {
        private const string sourceExtension = ".cs";
        private readonly ExportModule.IsTypeExcludedDelegate _isTypeExcluded;

        private string _outFile;

        internal XmlSerializerGenerator(Options options)
            : base(options)
        {
            _isTypeExcluded = options.IsTypeExcluded;
            _outFile = options.OutputFileArg;
        }

        internal void GenerateCode(List<Assembly> assemblies)
        {
            if (!string.IsNullOrEmpty(_outFile) && assemblies.Count > 1)
            {
                ToolConsole.WriteWarning(SR.Format(SR.WrnOptionConflictsWithInput, Options.Cmd.Out));
                _outFile = null;
            }

            foreach (Assembly assembly in assemblies)
            {
                GenerateCode(assembly);
            }
        }

        private void GenerateCode(Assembly assembly)
        {
            List<XmlMapping> mappings = new List<XmlMapping>();
            List<Type> types = CollectXmlSerializerTypes(assembly, mappings);

            if (types.Count == 0)
            {
                ToolConsole.WriteWarning(SR.Format(SR.WrnNoServiceContractTypes, assembly.GetName().CodeBase));
                return;
            }
            if (mappings.Count == 0)
            {
                ToolConsole.WriteWarning(SR.Format(SR.WrnNoXmlSerializerOperationBehavior, assembly.GetName().CodeBase));
                return;
            }

            bool success = false;
            bool toDeleteFile = true;

            string codePath = Path.GetTempFileName();

            try
            {
                if (File.Exists(codePath))
                {
                    File.Delete(codePath);
                }

                using (FileStream fs = File.Create(codePath))
                {
                    MethodInfo method = typeof(System.Xml.Serialization.XmlSerializer).GetMethod("GenerateSerializer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method == null)
                    {
                        throw new ToolRuntimeException(SR.GenerateSerializerNotFound);
                    }
                    else
                    {
                        success = (bool)method.Invoke(null, new object[] { types.ToArray(), mappings.ToArray(), fs });
                    }
                }
            }
            finally
            {
                if (!success && toDeleteFile && File.Exists(codePath))
                {
                    File.Delete(codePath);
                }
            }

            string sgenSource = XmlSerializer.GetXmlSerializerAssemblyName(types[0]);

            // delete all temp files generated by CodeDom except source file 
            sgenSource = BuildFilePath(sgenSource, sourceExtension, null);
            if (File.Exists(sgenSource))
                File.Delete(sgenSource);

            string sourceName;
            if (_outFile != null)
                sourceName = FilenameHelper.UniquifyFileName(_outFile, sourceExtension);
            else
                sourceName = FilenameHelper.UniquifyFileName(sgenSource, sourceExtension);

            string sourceFilePath = BuildFilePath(sourceName, sourceExtension, null);
            CreateDirectoryIfNeeded(sourceFilePath);
            File.Copy(codePath, sourceFilePath, true);
            ToolConsole.WriteLine(sourceFilePath);

            return;
        }

        private List<Type> CollectXmlSerializerTypes(Assembly assembly, List<XmlMapping> mappings)
        {
            List<Type> types = new List<Type>();

            ExportModule.ContractLoader contractLoader = new ExportModule.ContractLoader(new Assembly[] { assembly }, _isTypeExcluded);
            contractLoader.ContractLoadErrorCallback = delegate (Type contractType, string errorMessage)
                    {
                        ToolConsole.WriteWarning(SR.Format(SR.WrnUnableToLoadContractForSGen, contractType, errorMessage));
                    };

            foreach (ContractDescription contract in contractLoader.GetContracts())
            {
                types.Add(contract.ContractType);
                foreach (OperationDescription operation in contract.Operations)
                {
                    XmlSerializerOperationBehavior behavior = operation.Behaviors.Find<XmlSerializerOperationBehavior>();
                    if (behavior != null)
                    {
                        foreach (XmlMapping map in behavior.GetXmlMappings())
                        {
                            mappings.Add(map);
                        }
                    }
                }
            }
            return types;
        }
    }
}