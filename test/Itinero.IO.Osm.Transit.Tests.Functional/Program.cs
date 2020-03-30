using System;
using System.IO;
using System.Threading.Tasks;
using Itinero.IO.Shape;
using Itinero.Profiles;
using OsmSharp.Streams.Filters;
using Vehicle = Itinero.Osm.Vehicles.Vehicle;

namespace Itinero.IO.Osm.Transit.Tests.Functional
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // download test data.
            var transitOsmFile = await Download.Get("http://planet.anyways.eu/transit/OSM/transit.osm.bin");
            var osmFile = await
                Download.Get("http://planet.anyways.eu/planet/extracts/belgium-and-neighbourhood-latest.osm.pbf");
            
            // build routerdb and take into account the transit data.
            AddFromSourceOsmData(osmFile, transitOsmFile);
        }

        static void AddFromSourceOsmData(string osmFile, string osmTransitFile)
        {
            // load transit data formatted as osm data.
            using var transitSource = File.OpenRead(osmTransitFile);
            var osmTransitSource = new OsmSharp.Streams.BinaryOsmStreamSource(transitSource);

            using var source = File.OpenRead(osmFile);
            var osmSource = new OsmSharp.Streams.PBFOsmStreamSource(source);

            var merged = new OsmSharp.Streams.Filters.OsmStreamFilterMerge();
            merged.RegisterSource(osmSource);
            merged.RegisterSource(osmTransitSource);
            
            // run through transit handler filter.
            var transitFilter = new TransitDataHandlerOsmStream((osmGeo) =>
            {
                if (osmGeo.Tags != null && osmGeo.Tags.Contains("source", "ANYWAYS:transitdb")) return true;

                return false;
            });
            transitFilter.RegisterSource(merged);
            
            // build routerdb.
            var routerDb = new RouterDb();
            routerDb.LoadOsmData(transitFilter, Vehicle.Bicycle);
         
            // after routerdb is finished, use filtered data to properly add transit data.
            routerDb.AddPublicTransport(transitFilter, new IProfileInstance[] {Vehicle.Bicycle.Fastest()});
            
            // write the result.
            routerDb.WriteToShape("output.shp");
        }
    }
}
