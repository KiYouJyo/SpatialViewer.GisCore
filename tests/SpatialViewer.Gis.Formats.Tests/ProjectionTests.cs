using SpatialViewer.Gis.Core;
using SpatialViewer.Gis.Projections;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class ProjectionTests
{
    [Fact]
    public void ParsesWkt1AndWkt2EpsgIdentifiers()
    {
        var wkt1 = "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\"],AUTHORITY[\"EPSG\",\"4326\"]]";
        var wkt2 = "PROJCRS[\"WGS 84 / Pseudo-Mercator\",BASEGEOGCRS[\"WGS 84\",ID[\"EPSG\",4326]],ID[\"EPSG\",3857]]";

        var first = SpatialReferenceParser.ParseWkt(wkt1);
        var second = SpatialReferenceParser.ParseWkt(wkt2);

        Assert.Equal("WGS 84", first.Name);
        Assert.Equal("EPSG", first.Authority);
        Assert.Equal("4326", first.Code);
        Assert.Equal("EPSG", second.Authority);
        Assert.Equal("3857", second.Code);
    }

    [Fact]
    public void KeepsUnknownWktWithoutInventingAuthority()
    {
        const string wkt = "LOCAL_CS[\"Engineering Grid\"]";

        var parsed = SpatialReferenceParser.ParseWkt(wkt);

        Assert.Null(parsed.Authority);
        Assert.Null(parsed.Code);
        Assert.Equal(wkt, parsed.WellKnownText);
        Assert.Equal("Engineering Grid", parsed.Name);
    }

    [Fact]
    public void TransformsWgs84ToWebMercatorAndBackWithoutDroppingZm()
    {
        var service = new ManagedCoordinateTransformService();
        var source = SpatialReference.FromEpsg(4326);
        var target = SpatialReference.FromEpsg(3857);
        var coordinate = new GisCoordinate(1, 0, 12, 34);

        var projected = service.Transform(coordinate, source, target);
        var restored = service.Transform(projected, target, source);

        Assert.InRange(projected.X, 111319.4907, 111319.4909);
        Assert.InRange(projected.Y, -0.000001, 0.000001);
        Assert.Equal(12, projected.Z);
        Assert.Equal(34, projected.M);
        Assert.InRange(restored.X, 0.999999999, 1.000000001);
        Assert.InRange(restored.Y, -0.000000001, 0.000000001);
        Assert.Equal(12, restored.Z);
        Assert.Equal(34, restored.M);
    }

    [Fact]
    public void AuthorityAxisPolicyTreatsEpsg4326AsLatitudeLongitude()
    {
        var service = new ManagedCoordinateTransformService();
        var source = SpatialReference.FromEpsg(4326);
        var target = SpatialReference.FromEpsg(3857);

        var traditional = service.Transform(new GisCoordinate(1, 0), source, target);
        var authority = service.Transform(
            new GisCoordinate(0, 1),
            source,
            target,
            GisAxisOrderPolicy.AuthorityCompliant);

        Assert.InRange(Math.Abs(traditional.X - authority.X), 0, 0.000001);
        Assert.InRange(Math.Abs(traditional.Y - authority.Y), 0, 0.000001);
    }

    [Fact]
    public void RejectsLatitudeOutsideWebMercatorDomain()
    {
        var service = new ManagedCoordinateTransformService();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.Transform(
                new GisCoordinate(0, 90),
                SpatialReference.FromEpsg(4326),
                SpatialReference.FromEpsg(3857)));
    }

    [Fact]
    public void UnsupportedEpsgPairIsExplicit()
    {
        var service = new ManagedCoordinateTransformService();

        Assert.False(service.CanTransform(SpatialReference.FromEpsg(4326), SpatialReference.FromEpsg(32651)));
        Assert.Throws<NotSupportedException>(
            () => service.Transform(
                new GisCoordinate(120, 30),
                SpatialReference.FromEpsg(4326),
                SpatialReference.FromEpsg(32651)));
    }
}
