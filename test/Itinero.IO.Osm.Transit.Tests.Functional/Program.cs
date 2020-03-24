using System;
using System.IO;
using Itinero.IO.Shape;
using Itinero.Profiles;

namespace Itinero.IO.Osm.Transit.Tests.Functional
{
    class Program
    {
        static void Main(string[] args)
        {
            // load routerdb.
            var routerDb = RouterDb.Deserialize(
                File.OpenRead(@"/data/work/data/OSM/belgium.routerdb"));
            
            // extract profiles.
            var pedestrian = routerDb.GetSupportedProfile("pedestrian");
            var bicycle = routerDb.GetSupportedProfile("bicycle");
            var car = routerDb.GetSupportedProfile("car");
            
            // load transit data formatted as osm data.
            using var source = File.OpenRead(@"/data/work/data/pt-osm/transit.osm");
            var osmSource = new OsmSharp.Streams.XmlOsmStreamSource(source);
            
            // add pt links to routerdb.
            routerDb.AddPublicTransport(osmSource, new IProfileInstance[] { pedestrian, bicycle, car });
            
            // compress the routerdb.
            routerDb.Compress();
            
            // write the result.
            routerDb.WriteToShape("output.shp");
        }
    }
}
