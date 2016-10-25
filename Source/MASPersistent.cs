﻿/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2016 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using System;
using System.Collections.Generic;
using System.Text;

namespace AvionicsSystems
{
    /// <summary>
    /// The MASPersistent class uses the KSP ScenarioModule to save and restore
    /// persistent variables for all MASFlightComputer objects in the game.  It
    /// does so by getting a reference to the dictionary that the MAS FC uses to
    /// track persistents, so that it can write that dictionary during OnSave,
    /// and so it can replace the dictionary in the flight computer during the
    /// flight computer's Start().
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames, GameScenes.FLIGHT)]
    class MASPersistent : ScenarioModule
    {
        /// <summary>
        /// Dictionary of all registered MASFlightComputers with references to their persistent data.
        /// </summary>
        static private Dictionary<Guid, Dictionary<string, object>> knownPersistents = new Dictionary<Guid, Dictionary<string, object>>();

        /// <summary>
        /// Static boolean used to indicate when the ScenarioModule has had a chance to initialize.
        /// </summary>
        static internal bool PersistentsLoaded
        {
            get;
            private set;
        }

        /// <summary>
        /// OnAwake does some sanity-checks (make sure the PersistentsLoaded field is false,
        /// and that the persistents dictionary is cleared.
        /// </summary>
        public override void OnAwake()
        {
            //Utility.LogMessage(this, "OnAwake(): ");
            PersistentsLoaded = false;
            knownPersistents.Clear();
        }

        /// <summary>
        /// OnDestroy clears the persistents dictionary and marks the module as un-ready.
        /// </summary>
        private void OnDestroy()
        {
            //Utility.LogMessage(this, "OnDestroy(): ");
            PersistentsLoaded = false;
            knownPersistents.Clear();
        }

        /// <summary>
        /// Restore all persistent variables stored in persistent file.
        /// </summary>
        /// <param name="node"></param>
        public override void OnLoad(ConfigNode node)
        {
            ConfigNode[] fc = node.GetNodes("MASFlightComputer");
            Utility.LogMessage(this, "OnLoad(): {0} stored flight computers", fc.Length);

            for (int fcNodeIndex = fc.Length - 1; fcNodeIndex >= 0; --fcNodeIndex)
            {
                string fcId = string.Empty;
                if (fc[fcNodeIndex].TryGetValue("__MASFlightComputerId", ref fcId) && !string.IsNullOrEmpty(fcId))
                {
                    //Utility.LogMessage(this, "Found a node for flight computer {0}", fcId);

                    int numValues = fc[fcNodeIndex].CountValues;
                    Dictionary<string, object> persistentVars = new Dictionary<string, object>(numValues - 1);

                    // NOTE: We stop at 1, not 0, because we expect __MASFlightComputerId
                    // to be the first entry; it will be as long as no one manually reordered it.
                    for (int i = numValues - 1; i >= 1; --i)
                    {
                        ConfigNode.Value val = fc[fcNodeIndex].values[i];

                        string[] value = val.value.Split(',');
                        if (value.Length > 2)
                        {
                            string s = value[1].Trim();
                            for (int j = 2; j < value.Length; ++j)
                            {
                                s = s + ',' + value[i].Trim();
                            }
                            value[1] = s;
                        }

                        switch (value[0].Trim())
                        {
                            case "System.String":
                                persistentVars[val.name.Trim()] = value[1].Trim();
                                break;
                            case "System.Double":
                                double dbl = 0;
                                if (Double.TryParse(value[1].Trim(), out dbl))
                                {
                                    persistentVars[val.name.Trim()] = dbl;
                                }
                                else
                                {
                                    Utility.LogErrorMessage(this, "Failed to parse {0} ({1}) as a double", val.name, value[1].Trim());
                                }
                                break;
                            default:
                                Utility.LogErrorMessage(this, "Found unknown persistent type {0}", value[0]);
                                break;
                        }
                        //Utility.LogMessage(this, "{0} = {1}", val.name.Trim(), persistentVars[val.name.Trim()]);
                    }

                    knownPersistents.Add(new Guid(fcId), persistentVars);
                }
            }

            PersistentsLoaded = true;
        }

        /// <summary>
        /// When saving, write out all MASFlightComputer persistent values.
        /// </summary>
        /// <param name="node"></param>
        public override void OnSave(ConfigNode node)
        {
            Utility.LogMessage(this, "OnSave(): {0} registered flight computers", knownPersistents.Count);

            HashSet<string> knownVessels = new HashSet<string>();
            List<Vessel> vessels = FlightGlobals.Vessels;
            for (int idx = vessels.Count - 1; idx >= 0; --idx)
            {
                knownVessels.Add(vessels[idx].id.ToString());
            }

            foreach (var fc in knownPersistents)
            {
                if (fc.Value.ContainsKey(MASFlightComputer.vesselIdLabel) && knownVessels.Contains(fc.Value[MASFlightComputer.vesselIdLabel].ToString()))
                {
                    ConfigNode saveNode = new ConfigNode("MASFlightComputer");
                    saveNode.AddValue("__MASFlightComputerId", fc.Key.ToString());

                    foreach (var keyValPair in fc.Value)
                    {
                        string value = string.Format("{0},{1}", keyValPair.Value.GetType().ToString(), keyValPair.Value.ToString());
                        saveNode.AddValue(keyValPair.Key, value);
                    }

                    node.AddNode(saveNode);
                }
                else
                {
                    if (fc.Value.ContainsKey(MASFlightComputer.vesselIdLabel))
                    {
                        Utility.LogMessage(this, "Discarding flight computer {0} because vessel ID {1} no longer exists",
                        fc.Key,
                        fc.Value[MASFlightComputer.vesselIdLabel]);
                    }
                    else
                    {
                        Utility.LogMessage(this, "Discarding flight computer {0} because it did not stored vessel ID",
                            fc.Key);
                    }
                }
            }
        }

        /// <summary>
        /// Accessor method to restore persistent variables from our global table.
        /// If the MASFlightComputer that calls this method isn't already in our
        /// table, add it now.  Otherweise, return our restored persistent data.
        /// </summary>
        /// <param name="fcId">GUID of the MASFlightComputer</param>
        /// <param name="existingPersistents">An existing persistent table.  If the fcId isn't
        /// found in the dictionary, this persistent dictionary will be used.</param>
        /// <returns>The persistent dictionary that the MASFlightComputer should use.</returns>
        internal static Dictionary<string, object> RestoreDictionary(Guid fcId, Dictionary<string, object> existingPersistents)
        {
            if (!knownPersistents.ContainsKey(fcId))
            {
                knownPersistents[fcId] = existingPersistents;
            }
            return knownPersistents[fcId];
        }
    }
}
