using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.DataExchange.Core;
using Autodesk.DataExchange.Core.Models;
using DataExchangeGrasshopperConnector.Read;
using DataExchangeGrasshopperConnector.Util;
using Autodesk.DataExchange.Models;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Autodesk.DataExchange.DataModels;
using Autodesk.DataExchange.Insights;
using Autodesk.DataExchange.SchemaObjects.Relationships;
using Autodesk.DataExchange.SchemaObjects.Assets;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataExchangeGrasshopperConnector.Nodes
{
    public class ReceiveExchange : GH_Component
    {
        public NodeStatus ReceiveExchangeActionStatus = new NodeStatus();
        public bool ReceiveExchangeInProcess = false;
        private readonly CreateDXData dxData = new CreateDXData();
        List<ExchangeDataInfo> exchangeDataInfoList = null;
        public ExchangeDataInfo ExchangeDataInfo = new ExchangeDataInfo();
        public bool IsCurrentExchangeDataAndStepFileReady = false;
        public ExchangeData CurrentExchangeData = null;
        public string StepFilePath = null;
        public string RootAssetName = null;
        public bool IsMeshReading = false;
        public ElementDataModel RevitExchangeData = null;
        public List<InstanceObject> InstanceObjectList = new List<InstanceObject>();
        public DataTree<object> NewDXDataTree = new DataTree<object>();
        public List<RhinoObject> ObjectList = new List<RhinoObject>();
        public DataTree<GeometryBase> MeshGeometryDataTree = new DataTree<GeometryBase>();
        public DataTree<KeyValuePair<string, string>> MeshParamDataTree = new DataTree<KeyValuePair<string, string>>();
        public DataTree<object> MeshDXDataTree = new DataTree<object>();
        public bool IsExceptionThere = false;
        public string ExceptionMassage = null;
        public string FirstRev = null;
        /// <summary>
        /// Initializes a new instance of the ReceiveExchange class.
        /// </summary>
        public ReceiveExchange()
          : base("Receive Exchange", "Receive Exchange",
              "Receives data from a Data Exchange",
              "Data Exchange", "Send-Receive")
        {
        }
        public override void CreateAttributes()
        {
            m_attributes = new DataExchangeGrasshopperConnector.CustomUI.NodeActionButton(ReceiveExchangeActionStatus, this, "Receive", RunReceiveExchangeFunctionOnClick);
        }
        /// <summary>
        /// Receive Button Action
        /// </summary>
        public void RunReceiveExchangeFunctionOnClick()
        {
            ReceiveExchangeActionStatus.Status = true;
            ReceiveExchangeActionStatus.Message = "Receiving...";
            ExpireSolution(true);
        }




        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        /// <param name="pManager">The pManager object is used to set inputs.</param>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Exchange", "Exchange", "Data Exchange to receive data from.Specify with Get Exchange", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        /// <param name="pManager">The pManager object is used to set outputs.</param>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("DXData", "DXData", "Object of geometry and parameter", GH_ParamAccess.item);
        }


    /// <summary>
    /// This is the method that work before solve instance.
    /// </summary>
    protected override void BeforeSolveInstance()
        {
            if ((Params.Input[0].SourceCount <= 0) && (!ReceiveExchangeInProcess))
            {
                ReceiveExchangeInProcess = false;
                ReceiveExchangeActionStatus.Message = " ";
                ReceiveExchangeActionStatus.IsInputReady = false;
                IsCurrentExchangeDataAndStepFileReady = false;
                CurrentExchangeData = null;
                StepFilePath = null;
                dxData.DxDataTree = null;

            }
        }

//--------------------------------------------------------------------JSON Function--------------------------------------------------------------------------------------------
        public void WriteJsonFormat(List<Element> elementList)
        {
            JArray elementArray = new JArray();

            foreach (var categoryGroup in elementList.GroupBy(e => e.Category))
            {
                JArray data = new JArray();

                foreach (var element in categoryGroup)
                {
                    float[] matrix = element.Asset.Transformation.MatrixRepresentation.Elements;

                    JObject transformationObj = new JObject();
                    for (int i = 0; i < 4; i++)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            var value = matrix[i * 4 + j];
                            transformationObj.Add(new JProperty($"transformationObject.M{i}{j}", (double)value));
                        }
                    }

                    JObject elementObj = new JObject(
                        new JProperty("ElementName", element.Name),
                        new JProperty("Transformation", transformationObj)
                    );

                    data.Add(elementObj);
                }

                JObject categoryObj = new JObject(
                    new JProperty("Category", categoryGroup.Key),
                    new JProperty("Data", data)
                );

                elementArray.Add(categoryObj);
            }

            JObject elementsObj = new JObject(new JProperty("Element", elementArray));

            string jsonString = elementsObj.ToString(Formatting.Indented);

            File.WriteAllText(@"D:\Rohit\Incubation\PSET Work\grasshopper-connector-Dev\DataExchangeGrasshopperConnector.Test\MockData\Element3.json", jsonString);
        }

        //----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="dataAccess">TheDA object is used to retrieve from inputs and store in outputs.</param>

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            TimeStamp timeStamp = new TimeStamp();
            Logger logger = Logger.GetInstance();
            timeStamp.TimerStart();
            logger.WriteLogFile("Information", "[RECEIVE_EXCHANGE : SolveInstance()]", "Execution Started : Accepting Exchange as an input", null);

            if (!IsExceptionThere)
            {
                exchangeDataInfoList = new List<ExchangeDataInfo>();
                DA.GetDataList(0, exchangeDataInfoList);
                if (exchangeDataInfoList.Count > 1)
                {
                    if (ReceiveExchangeInProcess)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Already In Process!");
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Multiple Exchange Input Not Allowed...");
                        ExchangeDataInfo = new ExchangeDataInfo();
                        ReceiveExchangeInProcess = false;
                        ReceiveExchangeActionStatus.Message = " ";
                        ReceiveExchangeActionStatus.IsInputReady = false;
                        IsCurrentExchangeDataAndStepFileReady = false;
                        CurrentExchangeData = null;
                        StepFilePath = null;
                        dxData.DxDataTree = null;
                        NewDXDataTree = new DataTree<object>();
                        ObjectList = new List<RhinoObject>();
                        MeshGeometryDataTree = new DataTree<GeometryBase>();
                        MeshDXDataTree = new DataTree<object>();
                        IsExceptionThere = false;
                        ExceptionMassage = null;
                    }
                }
                else if ((exchangeDataInfoList.Count == 1) && (exchangeDataInfoList.ElementAt(0).ApplicationMode == ApplicationMode.ReadOnly))
                {
                    ExchangeDataInfo exchangeData = exchangeDataInfoList.ElementAt(0);
                    if ((ExchangeDataInfo.ExchangeItem != null) && (exchangeData.ExchangeItem.ExchangeID != ExchangeDataInfo.ExchangeItem.ExchangeID) && (!ReceiveExchangeInProcess))
                    {
                        ExchangeDataInfo = exchangeData;
                        ReceiveExchangeActionStatus.Message = " ";
                        ReceiveExchangeActionStatus.IsInputReady = true;
                        IsCurrentExchangeDataAndStepFileReady = false;
                        CurrentExchangeData = null;
                        StepFilePath = null;
                        NewDXDataTree = new DataTree<object>();
                        ObjectList = new List<RhinoObject>();
                        MeshGeometryDataTree = new DataTree<GeometryBase>();
                        MeshDXDataTree = new DataTree<object>();
                        IsExceptionThere = false;
                        ExceptionMassage = null;
                    }
                    else if (ExchangeDataInfo.ExchangeItem == null)
                    {
                        ExchangeDataInfo = exchangeData;
                        ReceiveExchangeActionStatus.Message = " ";
                        ReceiveExchangeActionStatus.IsInputReady = true;
                        IsCurrentExchangeDataAndStepFileReady = false;
                        CurrentExchangeData = null;
                        StepFilePath = null;
                        NewDXDataTree = new DataTree<object>();
                        ObjectList = new List<RhinoObject>();
                        MeshGeometryDataTree = new DataTree<GeometryBase>();
                        MeshDXDataTree = new DataTree<object>();
                        IsExceptionThere = false;
                        ExceptionMassage = null;
                    }
                    else if (exchangeData.ExchangeItem.ExchangeID == ExchangeDataInfo.ExchangeItem.ExchangeID)
                    {
                        ReceiveExchangeActionStatus.IsInputReady = true;
                    }
                }
                if (ReceiveExchangeActionStatus.Status)
                {
                    ExchangeDataInfo = exchangeDataInfoList.ElementAt(0);
                    if (ExchangeDataInfo.ApplicationMode == ApplicationMode.ReadOnly)
                    {
                        ExchangeItem exchangeItem = ExchangeDataInfo.ExchangeItem;
                        DataExchangeIdentifier exchangeIdentifier = ExchangeDataInfo.ExchangeIdentifier;
                        ReadExchangesData readWriteExchangeData = ReadExchangesData.GetInstance();
                        GetGeometry getGeometry = new GetGeometry();
                        GetParameters getParameters = new GetParameters();
                        if (IsCurrentExchangeDataAndStepFileReady)
                        {
                            if (StepFilePath != null && CurrentExchangeData != null)
                            {
                                LoadExchange loadExchange = new LoadExchange();

                                if (!IsMeshReading)
                                {
                                    RevitExchangeData = Autodesk.DataExchange.DataModels.ElementDataModel.Create(readWriteExchangeData.Client, CurrentExchangeData);
                                    string rootAssetName = RevitExchangeData.ExchangeData.RootAsset.Id;

                                    InstanceObjectList = loadExchange.LoadExchangeInRhino(StepFilePath, rootAssetName);

                                    MeshDXDataTree = new DataTree<object>();
                                    MeshGeometryDataTree = new DataTree<GeometryBase>();
                                    ObjectList = new List<RhinoObject>();

                                    IsMeshReading = true;
                                    Metrics.StartTracking("RecieveExchange:DownloadMeshGeometry");

                                    DownloadMeshGeometry();
                                    Metrics.StopTracking("RecieveExchange:DownloadMeshGeometry");

                                }
                                else
                                {
                                    ExchangeElements exchangeElements = new ExchangeElements();
                                    List<Element> elementList = exchangeElements.GetElementList(RevitExchangeData);

                                    //-----------------------------------------------------------------------------------------------------------

                                    WriteJsonFormat(elementList);

                                    //-----------------------------------------------------------------------------------------------------------

                                    bool isIdPresent = elementList.Any(obj => obj.InstanceParameters.Any(x => x.Name == "Element Index"));

                                    if (isIdPresent)
                                    {
                                        elementList = FilterElementIndex(elementList);

                                    }
                                    else
                                    {
                                        elementList.Sort((obj1, obj2) => string.Compare(obj1.Asset.Id, obj2.Asset.Id, StringComparison.Ordinal));
                                    }

                                    DXDataTree dxDataTree = new DXDataTree();
                                    DataTree<object> newDXDataTree = dxDataTree.GetDXDataTree(InstanceObjectList, elementList, readWriteExchangeData, CurrentExchangeData);

                                    loadExchange.DeleteInstanceDefinitions();

                                    DataTree<GeometryBase> geometryBaseDataTree = new DataTree<GeometryBase>();
                                    DataTree<KeyValuePair<string, string>> parameterDataTree = new DataTree<KeyValuePair<string, string>>();

                                    if (newDXDataTree != null && newDXDataTree.Branches.Count > 0 && newDXDataTree.Branches[0].Count >= 3)
                                    {
                                        //geometry
                                        List<RhinoObject> rhinoObjectList = getGeometry.GetRhinoObjectList(newDXDataTree);
                                        rhinoObjectList.AddRange(ObjectList);
                                        getGeometry.CreateBoundingBox(rhinoObjectList);
                                        geometryBaseDataTree = getGeometry.GetGeometryBaseDataTreeOfObject(newDXDataTree);
                                    }

                                    int oldGeometryBranchCounter = geometryBaseDataTree.Branches.Count;

                                    for (int branchCounter = 0; branchCounter < MeshGeometryDataTree.Branches.Count; branchCounter++)
                                    {
                                        GH_Path gH_Path = new GH_Path(oldGeometryBranchCounter);

                                        geometryBaseDataTree.AddRange(MeshGeometryDataTree.Branches[branchCounter], gH_Path);
                                        oldGeometryBranchCounter++;
                                    }

                                    int oldDXDataTreeBranchCounter = newDXDataTree.Branches.Count;
                                    for (int branchCounter = 0; branchCounter < MeshGeometryDataTree.Branches.Count; branchCounter++)
                                    {
                                        GH_Path gH_Path = new GH_Path(oldDXDataTreeBranchCounter);
                                        newDXDataTree.AddRange(MeshDXDataTree.Branches[branchCounter], gH_Path);
                                        oldDXDataTreeBranchCounter++;
                                    }

                                   


                                    //parameter
                                    if (MeshParamDataTree.Branches.Count != 0)
                                    {
                                        DataTree<object> c3dPrimitivesDXDataTree = new DataTree<object>();
                                        var branches = newDXDataTree.Branches;
                                        int c3dPrimitivesDXDataTreeCounter = 0;
                                        foreach (List<object> selectedBranch in branches)
                                        {

                                            if (selectedBranch[1].GetType().Name == "InstanceObject")
                                            {
                                                GH_Path c3dPath = new GH_Path(c3dPrimitivesDXDataTreeCounter);
                                                c3dPrimitivesDXDataTree.AddRange(selectedBranch, c3dPath);
                                                c3dPrimitivesDXDataTreeCounter++;

                                            }

                                        }

                                        if (c3dPrimitivesDXDataTree.Branches.Count != 0)
                                        {
                                            List<Element> newFilteredElementList = exchangeElements.GetElementListFromDataTree(c3dPrimitivesDXDataTree);
                                            parameterDataTree = getParameters.GetParameterDataTreeOfKeyValue(newFilteredElementList);
                                            int oldparameterDataTree = parameterDataTree.Branches.Count;
                                            
                                            for (int branchCounter = 0; branchCounter < MeshParamDataTree.Branches.Count; branchCounter++)
                                            {
                                                GH_Path gH_Path = new GH_Path(oldparameterDataTree);
                                                parameterDataTree.AddRange(MeshParamDataTree.Branches[branchCounter], gH_Path);
                                                oldparameterDataTree++;
                                            }
                                                

                                        }
                                        else
                                        {
                                            parameterDataTree = MeshParamDataTree;
                                        }

                                        MeshParamDataTree.Clear();
                                        
                                    }
                                    else
                                    {
                                        List<Element> newFilteredElementList = exchangeElements.GetElementListFromDataTree(newDXDataTree);
                                        parameterDataTree = getParameters.GetParameterDataTreeOfKeyValue(newFilteredElementList);
                                    }
                                   

                                    dxData.DxDataTree = newDXDataTree;
                                    dxData.AllTypeGeometryDataTree = geometryBaseDataTree;
                                    dxData.AllTypeGeometryParaDataTree = parameterDataTree;

                                    //geometry
                                    if (!(newDXDataTree != null) && !(newDXDataTree.Branches.Count > 0) && !(newDXDataTree.Branches[0].Count >= 3))
                                    {
                                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "DXData is Empty!");
                                        TimeSpan Stop = timeStamp.TimerStop();
                                        logger.WriteLogFile("Error", "[RECEIVE_EXCHANGE : SolveInstance()]", "Execution Stop : DXData is empty." + "|" + Stop, null);
                                    }


                                    IsMeshReading = false;
                                    ReceiveExchangeActionStatus.Status = false;
                                    ReceiveExchangeActionStatus.Message = "Completed";
                                    IsCurrentExchangeDataAndStepFileReady = false;
                                    CurrentExchangeData = null;
                                    StepFilePath = null;
                                    ReceiveExchangeInProcess = false;
                                }
                            }
                            else
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Exchange Downloading Fail!");
                                ReceiveExchangeActionStatus.Status = false;
                                dxData.AllTypeGeometryDataTree = null;
                                dxData.AllTypeGeometryParaDataTree = null;
                                ReceiveExchangeActionStatus.Message = "Error";
                                IsCurrentExchangeDataAndStepFileReady = false;
                                CurrentExchangeData = null;
                                StepFilePath = null;
                                ReceiveExchangeInProcess = false;
                            }
                        }
                        else
                        {
                            if (ReceiveExchangeInProcess == false)
                            {
                                ReceiveExchangeInProcess = true;
                                ReadExchangesData.GetInstance().GetCurrentExchangeDataAndStepFile(exchangeItem, exchangeIdentifier, this);
                            }
                            else
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Already In Process!");
                            }
                        }
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid Node Input !");
                        TimeSpan Failed = timeStamp.TimerStop();
                        logger.WriteLogFile("Warning", "[RECEIVE_EXCHANGE : SolveInstance()]", "Execution Failed : Invalid input" + "|" + Failed, null);
                        IsCurrentExchangeDataAndStepFileReady = false;
                        CurrentExchangeData = null;
                        StepFilePath = null;
                        ReceiveExchangeInProcess = false;
                    }
                }
                else
                {
                    dxData.AllTypeGeometryDataTree = null;
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ExceptionMassage);
                ReceiveExchangeActionStatus.Status = false;
                dxData.AllTypeGeometryDataTree = null;
                dxData.AllTypeGeometryParaDataTree = null;
                ReceiveExchangeActionStatus.Message = "Error";
                IsCurrentExchangeDataAndStepFileReady = false;
                CurrentExchangeData = null;
                StepFilePath = null;
                ReceiveExchangeInProcess = false;
            }
            if (dxData.AllTypeGeometryDataTree != null)
            {
                DA.SetData(0, dxData);
                TimeSpan output = timeStamp.TimerStop();
                logger.WriteLogFile("Information", "[RECEIVE_EXCHANGE : SolveInstance()]", "Execution Completed : DXData is set as an output" + "|" + output, null);
            }
        }

        private List<Element> FilterElementIndex(List<Element> elementList)
        {
            List<Element> FilteredListOfElements = new List<Element>();
            int count = 0;
            for (int Index = 0; Index < elementList.Count; Index++)
            {

                foreach (var element in elementList)
                {
                    foreach (var instanceParam in element.InstanceParameters)
                    {
                        if (instanceParam.Name == "Element Index")
                        {
                            if (instanceParam.Value is string)
                            {
                                if (instanceParam.Value == count.ToString())
                                {
                                    FilteredListOfElements.Add(element);

                                    count++;
                                    break;
                                }
                            }
                            else
                            {
                                if (instanceParam.Value == count)
                                {
                                    FilteredListOfElements.Add(element);

                                    count++;
                                    break;
                                }
                            }

                        }
                    }

                }
            }
            return FilteredListOfElements;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.ReceiveExchange;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("40ECC283-EF4D-488F-A8C2-7D9927A8C462"); }
        }

        /// <summary>
        /// Delegates the call to all parameters
        /// </summary>
        /// <param name="document">GH_Document Object</param>
        public override void AddedToDocument(GH_Document document)
        {
            ForgeAuthentication.GetForgeReadWriteAuthentication();
        }

        public async Task DownloadMeshGeometry()
        {
            ReadExchangesData readWriteExchangeData = ReadExchangesData.GetInstance();
            LoadExchange loadGeometryInRhino = new LoadExchange();
            List<RhinoObject> meshObjectList = new List<RhinoObject>();
            GetGeometry getGeometry = new GetGeometry();
            _ = new GetParameters();
            int branchCounter = 0;
            int meshBranchCounter = 0;
            int mBranchCounter = 0;
            DXDataTree dXDataTree = new DXDataTree();

            var dataModel = ElementDataModel.Create(readWriteExchangeData.Client, CurrentExchangeData);

            Element rootelement = dataModel.Elements.ToList()[0]; //todo for getting root element by rootasset other than c3d

            Metrics.StartTracking("DownloadMeshGeometry:GetFlattenedListOfElements");
            IEnumerable<Element>  allElements = ElementModelExtensions.GetFlattenedListOfElements(dataModel.Elements);
            Metrics.StopTracking("DownloadMeshGeometry:GetFlattenedListOfElements");

            List<Element> meshElements = new List<Element>();
            foreach (var element in allElements)
            {
                bool isMesh = IsMeshGeometryElement(element);
                if (isMesh)
                {
                    meshElements.Add(element);
                }
            }

            Metrics.StartTracking("DownloadMeshGeometry:GetElementGeometriesByElementsAsync");
            var elementGeometryListMapping = await dataModel.GetElementGeometriesByElementsAsync(meshElements);
            Metrics.StopTracking("DownloadMeshGeometry:GetElementGeometriesByElementsAsync");

            Transform xform = Transform.Identity;
            
            foreach (var kvPair in elementGeometryListMapping)
            {
                GH_Path mgH_Path = new GH_Path(mBranchCounter);
                var element = kvPair.Key;
                List<ElementGeometry> geometryList = kvPair.Value.ToList();

                List<Autodesk.DataExchange.DataModels.Parameter> nestedParameters = new List<Autodesk.DataExchange.DataModels.Parameter>();
                List<Autodesk.DataExchange.DataModels.Parameter> elementNestedParameters = new List<Autodesk.DataExchange.DataModels.Parameter>();
                List<KeyValuePair<Element, List<Autodesk.DataExchange.DataModels.Parameter>>> elementParametersMap = new List<KeyValuePair<Element, List<Autodesk.DataExchange.DataModels.Parameter>>>();
                //Call for this function to get list of parameters from nested element 
                GetParametersForNestedElements(element, ref nestedParameters, elementParametersMap, rootelement);
                List<KeyValuePair<string, string>> keyValueList = new List<KeyValuePair<string, string>>();

                //This will check element asset id and assign compare into elementParametersMap i.e. list of element with its parameters and get parameters for specific element
                foreach (var pair in elementParametersMap)
                {
                    if (pair.Key.Asset.Id.Equals(element.Asset.Id))
                    {
                        elementNestedParameters = pair.Value;

                        foreach (var parameter in elementNestedParameters)
                        {
                            if (parameter.ParameterDataType is Autodesk.DataExchange.Core.Enums.ParameterDataType.ParameterSet)
                            {
                                var parameterlist = (parameter as Autodesk.DataExchange.DataModels.ParameterSet).Parameters;
                                foreach (Autodesk.DataExchange.DataModels.Parameter c3dparameter in parameterlist)
                                {
                                    keyValueList.Add(new KeyValuePair<string, string>(c3dparameter.Name, Convert.ToString(c3dparameter.Value)));
                                }

                                // Add the key-value pairs to MeshParamDataTree
                                foreach (var keyValues in keyValueList)
                                {
                                    MeshParamDataTree.Add(keyValues, mgH_Path);
                                }

                                // Clear the list for the next iteration
                                keyValueList.Clear();
                            }
                        }
                        elementNestedParameters.Clear();

                    }


 
                }
                

                if (geometryList.Count > 0)
                {
                    foreach (var inGeometry in geometryList)
                    {
                        if ((inGeometry is Geometry) && (inGeometry as Geometry).FilePath.Contains(".obj"))
                        {
                            // Load mesh geometries in Rhino.
                            Geometry meshGeometry = inGeometry as Geometry;
                            meshObjectList.AddRange(loadGeometryInRhino.LoadMeshFileInRhino((inGeometry as Geometry).FilePath));
                            ObjectList.AddRange(meshObjectList);
                            xform = dXDataTree.ApplyTransformationFromMatrix(element, RhinoDoc.ActiveDoc.ModelUnitSystem, readWriteExchangeData, CurrentExchangeData);
                            var rhinoUnit = Utils.Unit.GetUnit(RhinoDoc.ActiveDoc.ModelUnitSystem.ToString());
                            UnitSystem unit = Utils.Unit.GetUnit(element.Asset.LengthUnit.Name);
                            double scaleFactor = Rhino.RhinoMath.UnitScale(unit, rhinoUnit);
                            xform = Transform.Multiply(xform, Transform.Scale(Point3d.Origin, scaleFactor));
                        }
                    }
                }

                if (meshObjectList.Count > 0)
                {
                    foreach (RhinoObject selectedhMeshObject in meshObjectList)
                    {
                        GH_Path meshGH_Path = new GH_Path(meshBranchCounter);
                        List<object> meshInfo = new List<object>
                        {
                            element,
                            selectedhMeshObject,
                            xform
                        };
                        MeshDXDataTree.AddRange(meshInfo, meshGH_Path);
                        meshBranchCounter++;
                    }
                    GH_Path gH_Path = new GH_Path(branchCounter);
                    MeshGeometryDataTree.AddRange(getGeometry.GetMeshGeometryDataList(meshObjectList, xform, element), gH_Path);
                    branchCounter++;

                }


                mBranchCounter++;
                meshObjectList.Clear();
               
            }
           

            ExpireSolution(true);
        }

        /// <summary>
        /// this function is to get list of nested element parameters
        /// </summary>
        /// <param name="element"> element from flattened list</param>
        /// <param name="nestedParameters"> list of nested parameters</param>
        /// <param name="elementParametersMap">list of keyvalue pair which contains element and its parameter list </param>
        /// <param name="rootelement"> root element of exchange</param>
        /// <returns>Null</returns>
        public void GetParametersForNestedElements(Element element, ref List<Autodesk.DataExchange.DataModels.Parameter> nestedParameters, List<KeyValuePair<Element, List<Autodesk.DataExchange.DataModels.Parameter>>> elementParametersMap, Element rootelement = null)
        {
            if (rootelement != null)
            {
                element = rootelement;
            }
            List<Element> childElements = element.GetChildElements().ToList();
            nestedParameters.AddRange(element.InstanceParameters);
            nestedParameters.AddRange(element.TypeParameters);
            if (childElements.Count > 0)
            {
                foreach (Element childElement in childElements)
                {
                    GetParametersForNestedElements(childElement, ref nestedParameters, elementParametersMap, null);
                }
            }
            else if (element != null && element.HasGeometry)
            {
                if (nestedParameters != null)
                {
                    List<Autodesk.DataExchange.DataModels.Parameter> newParameter = new List<Autodesk.DataExchange.DataModels.Parameter>(nestedParameters);
                    elementParametersMap.Add(new KeyValuePair<Element, List<Autodesk.DataExchange.DataModels.Parameter>>(element, newParameter));
                }
            }

            //remove type parameters from list when back traversing
            int countTypeParameter = nestedParameters.Count - element.TypeParameters.Count;
            nestedParameters.RemoveRange(countTypeParameter, element.TypeParameters.Count);
            //remove instance parameters from list when back traversing
            int countInstanceParameter = nestedParameters.Count - element.InstanceParameters.Count;
            nestedParameters.RemoveRange(countInstanceParameter, element.InstanceParameters.Count);
        }


        /// <summary>
        /// to get only mesh elements from all elements
        /// </summary>
        /// <param name="element"> revit element </param>
        /// <returns> return true if element is tritonmesh </returns>
        public bool IsMeshGeometryElement(Element element)
        {
            try
            {
                var elementGeometries = new List<ElementGeometry>();
                var designAssets = element.Asset.ChildNodes.Where(nodeRel =>
                {
                    if (nodeRel.Relationship is ReferenceRelationship relationship && relationship.ModelStructure?.Value == true)
                    {
                        return true;
                    }
                    return false;
                }).Select(item => item.Node).OfType<DesignAsset>();

                foreach (var designAsset in designAssets)
                {
                    var geomAssets = designAsset.GetChildrenByType<GeometryAsset>();
                    List<GeometryAsset> cacheMissedGeomAssets = new List<GeometryAsset>();
                    foreach (var geomAsset in geomAssets)
                    {
                        if (geomAsset.Geometry.Geometry is Autodesk.DataExchange.SchemaObjects.Geometry.TritonMesh)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }
    }
}