using System;
using System.Collections.Generic;
using System.Linq;
using Itinero.Algorithms.Search.Hilbert;
using Itinero.Attributes;
using Itinero.Data.Network.Edges;
using Itinero.LocalGeo;
using Itinero.Profiles;
using OsmSharp;
using OsmSharp.Db;

namespace Itinero.IO.Osm.Transit
{
    public static class RouterDbExtensions
    {
        /// <summary>
        /// Adds the public transport data in the relations to add the public transport data to the routerdb.
        /// </summary>
        /// <param name="routerDb">The router db.</param>
        /// <param name="transitOsm">The transit osm data.</param>
        /// <param name="profiles">The profiles to resolve stops for.</param>
        public static void AddPublicTransport(this RouterDb routerDb, IEnumerable<OsmGeo> transitOsm, IProfileInstance[] profiles)
        {
            var members = new Dictionary<OsmGeoKey, OsmGeo>();
            
            // collect all relations.
            var relations = new List<Relation>();
            foreach (var osmGeo in transitOsm)
            {
                if (osmGeo.Type != OsmGeoType.Relation) continue;
                var relation = osmGeo as Relation;
                if (relation?.Tags == null || !relation.Tags.Contains("type", "route")) continue;
                if (!relation.Tags.TryGetValue("route", out var routeType)) continue;
                if (relation.Members == null || relation.Members.Length == 0) continue;
                
                relations.Add(relation);

                foreach (var member in relation.Members)
                {
                    var key = new OsmGeoKey(member.Type, member.Id);
                    members[key] = null;
                }
            }

            // collection members.
            foreach (var osmGeo in transitOsm)
            {
                if (osmGeo.Id == null) continue;
                
                var key = new OsmGeoKey(osmGeo.Type, osmGeo.Id.Value);
                
                if (!members.ContainsKey(key)) continue;

                members[key] = osmGeo;
            }
            
            routerDb.AddPublicTransport(relations,
                (key) =>
                {
                    if (!members.TryGetValue(key, out var value))
                    {
                        return null;
                    }

                    return value;
                }, profiles);
        }
        
        /// <summary>
        /// Adds the public transport data in the relations to add the public transport data to the routerdb.
        /// </summary>
        /// <param name="routerDb">The router db.</param>
        /// <param name="profiles">The profiles.</param>
        /// <param name="relations">The relations.</param>
        /// <param name="getOsmGeo">Gets the osm geo objects.</param>
        public static void AddPublicTransport(this RouterDb routerDb, IEnumerable<Relation> relations,
            Func<OsmGeoKey, OsmGeo> getOsmGeo, IProfileInstance[] profiles)
        {
            var router = new Router(routerDb);

            // resolve all stops.
            var stopsResolved = new Dictionary<long, RouterPoint>();
            foreach (var relation in relations)
            {
                if (relation?.Members == null) continue;

                foreach (var member in relation.Members)
                {
                    if (member.Type != OsmGeoType.Node) continue;
                    if (!(getOsmGeo(new OsmGeoKey(member.Type, member.Id)) is Node node)) continue;
                    if (node.Latitude == null || node.Longitude == null || !node.Id.HasValue) continue;

                    if (stopsResolved.ContainsKey(node.Id.Value)) continue;
                    
                    var resolve = router.TryResolve(profiles,
                        new Coordinate((float) node.Latitude, (float) node.Longitude));
                    if (resolve.IsError) continue;

                    stopsResolved[node.Id.Value] = resolve.Value;
                }
            }
            
            // add them as vertices.
            var stops = new List<(long nodeId, RouterPoint routerPoint)>(
                stopsResolved.Select(x => (x.Key, x.Value)));
            var vertices = routerDb.AddAsVertices(stops.Select(x => x.routerPoint).ToArray());
            routerDb.Network.Sort((v1, v2) =>
            {
                for (var i = 0; i < vertices.Length; i++)
                {
                    if (vertices[i] == v1)
                    {
                        vertices[i] = (uint)v2;
                    }
                    else if (vertices[i] == v2)
                    {
                        vertices[i] = (uint)v1;
                    }
                }
            });
            
            var stopVertices = new Dictionary<long, uint>();
            for (var i = 0; i < vertices.Length; i++)
            {
                stopVertices[stops[i].nodeId] = vertices[i];
            }

            // loop over all relations and it's way members.
            var routeTypeProfiles = new Dictionary<string, ushort>();
            foreach (var relation in relations)
            {
                if (relation.Tags == null || !relation.Tags.Contains("type", "route")) continue;
                if (!relation.Tags.TryGetValue("route", out var routeType)) continue;
                
                var edgeProfile = new AttributeCollection(
                    new Attributes.Attribute("type", "route"),
                    new Attributes.Attribute("route", routeType));
                
                if (relation?.Members == null) continue;

                foreach (var member in relation.Members)
                {
                    if (member.Type != OsmGeoType.Way) continue;
                    if (!(getOsmGeo(new OsmGeoKey(member.Type, member.Id)) is Way way)) continue;
                    if (way.Nodes == null || way.Nodes.Length < 0) continue;

                    var node1 = way.Nodes[0];
                    var node2 = way.Nodes[way.Nodes.Length - 1];
                    
                    if (!stopVertices.TryGetValue(node1, out var fromVertex) ||
                        !stopVertices.TryGetValue(node2, out var toVertex))
                    {
                        continue;
                    }

                    if (fromVertex == uint.MaxValue ||
                        toVertex == uint.MaxValue)
                    {
                        continue;
                    }
                    if (fromVertex == toVertex) continue;

                    var distance = Coordinate.DistanceEstimateInMeter(routerDb.Network.GetVertex(fromVertex),
                        routerDb.Network.GetVertex(toVertex));
                    if (distance > routerDb.Network.MaxEdgeDistance)
                    {
                        distance = routerDb.Network.MaxEdgeDistance;
                    }

                    if (!routeTypeProfiles.TryGetValue(routeType, out var edgeProfileId))
                    {
                        edgeProfileId = (ushort)routerDb.EdgeProfiles.Add(edgeProfile);
                        routeTypeProfiles[routeType] = edgeProfileId;
                    }

                    var edgeEnumerator = routerDb.Network.GetEdgeEnumerator();
                    if (edgeEnumerator.MoveTo(fromVertex) &&
                        edgeEnumerator.MoveNextUntil(e => e.To == toVertex))
                    {
                        // TODO: this link is there already, update its profile.
                        continue;
                    }
                    
                    routerDb.Network.AddEdge(fromVertex, toVertex, new EdgeData()
                    {
                        Distance = distance,
                        Profile = edgeProfileId,
                        MetaId = 0
                    }, null);
                }
            }
        }
    }
}