using System;
using System.Collections.Generic;
using OsmSharp;
using OsmSharp.Db;
using OsmSharp.Streams.Filters;

namespace Itinero.IO.Osm.Transit
{
    /// <summary>
    /// A stream that can be registered before the Itinero preprocessor. This will index all relevant transit data.
    /// </summary>
    public class TransitDataHandlerOsmStream : OsmStreamFilter
    {
        private readonly Dictionary<OsmGeoKey, OsmGeo> _members = new Dictionary<OsmGeoKey, OsmGeo>();
        private readonly Dictionary<long, Relation> _relations = new Dictionary<long, Relation>();
        private readonly Func<OsmGeo, bool> _filter;

        /// <summary>
        /// Creates a new transit data handler filter.
        /// </summary>
        /// <param name="filter">An extra filter to filter only specific public transport data.</param>
        public TransitDataHandlerOsmStream(Func<OsmGeo, bool> filter = null)
        {
            _filter = filter;
        }

        private bool _firstPass = true;
        
        /// <inheritdoc/>
        public override bool MoveNext(bool ignoreNodes, bool ignoreWays, bool ignoreRelations)
        {
            if (!this.Source.MoveNext(ignoreNodes, ignoreWays, ignoreRelations)) return false;

            var current = this.Current();
            if (current?.Id == null) return true;
            if (current.Type == OsmGeoType.Node)
            {
                _firstPass = false;
                
                var key = new OsmGeoKey(current.Type, current.Id.Value);
                if (!_members.ContainsKey(key)) return true;

                _members[key] = current;
            }
            else if (current.Type == OsmGeoType.Way)
            {
                
                var key = new OsmGeoKey(current.Type, current.Id.Value);
                if (!_members.ContainsKey(key)) return true;

                _members[key] = current;
            }
            else if (current.Type == OsmGeoType.Relation)
            {
                if (!_firstPass) return true;
                
                var relation = current as Relation;
                if (relation?.Id == null) return true;
                if (relation.Tags == null || !relation.Tags.Contains("type", "route")) return true;
                if (!relation.Tags.TryGetValue("route", out var routeType)) return true;
                if (relation.Members == null || relation.Members.Length == 0) return true;

                // apply extra filter.
                if (_filter != null && !_filter.Invoke(relation)) return true;
                
                _relations[relation.Id.Value] = relation;

                foreach (var member in relation.Members)
                {
                    var key = new OsmGeoKey(member.Type, member.Id);
                    _members[key] = null;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets all relevant transit objects.
        /// </summary>
        public IEnumerable<Relation> TransitObjects => _relations.Values;

        /// <summary>
        /// Gets the member for the given key. Returns null if member is not present.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The member if there.</returns>
        public OsmGeo GetMember(OsmGeoKey key)
        {
            if (!_members.TryGetValue(key, out var osmGeo)) return null;

            return osmGeo;
        }

        /// <inheritdoc/>
        public override OsmGeo Current()
        {
            return this.Source.Current();
        }
        
        /// <inheritdoc/>
        public override void Reset()
        {
            this.Source.Reset();
        }

        /// <inheritdoc/>
        public override bool CanReset => this.Source.CanReset;
    }
}