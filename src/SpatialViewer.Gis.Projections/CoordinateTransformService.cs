using SpatialViewer.Gis.Core;

namespace SpatialViewer.Gis.Projections;

public enum GisAxisOrderPolicy
{
    TraditionalGis,
    AuthorityCompliant,
}

public interface IGisCoordinateTransformService
{
    bool CanTransform(SpatialReference source, SpatialReference target);

    GisCoordinate Transform(
        GisCoordinate coordinate,
        SpatialReference source,
        SpatialReference target,
        GisAxisOrderPolicy axisOrderPolicy = GisAxisOrderPolicy.TraditionalGis);
}

public sealed class ManagedCoordinateTransformService : IGisCoordinateTransformService
{
    private const double EarthRadius = 6378137d;
    private const double MaxWebMercatorLatitude = 85.0511287798066d;

    public bool CanTransform(SpatialReference source, SpatialReference target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source == target)
        {
            return true;
        }

        return SpatialReferenceParser.TryGetEpsg(source, out var sourceEpsg) &&
               SpatialReferenceParser.TryGetEpsg(target, out var targetEpsg) &&
               ((sourceEpsg == 4326 && targetEpsg == 3857) ||
                (sourceEpsg == 3857 && targetEpsg == 4326));
    }

    public GisCoordinate Transform(
        GisCoordinate coordinate,
        SpatialReference source,
        SpatialReference target,
        GisAxisOrderPolicy axisOrderPolicy = GisAxisOrderPolicy.TraditionalGis)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source == target)
        {
            return coordinate;
        }

        if (!SpatialReferenceParser.TryGetEpsg(source, out var sourceEpsg) ||
            !SpatialReferenceParser.TryGetEpsg(target, out var targetEpsg))
        {
            throw new NotSupportedException("Both source and target spatial references must resolve to a supported EPSG code.");
        }

        return (sourceEpsg, targetEpsg) switch
        {
            (4326, 3857) => ForwardWebMercator(coordinate, axisOrderPolicy),
            (3857, 4326) => InverseWebMercator(coordinate, axisOrderPolicy),
            _ => throw new NotSupportedException($"Managed coordinate transformation EPSG:{sourceEpsg} -> EPSG:{targetEpsg} is not supported."),
        };
    }

    private static GisCoordinate ForwardWebMercator(
        GisCoordinate coordinate,
        GisAxisOrderPolicy axisOrderPolicy)
    {
        var longitude = axisOrderPolicy == GisAxisOrderPolicy.AuthorityCompliant
            ? coordinate.Y
            : coordinate.X;
        var latitude = axisOrderPolicy == GisAxisOrderPolicy.AuthorityCompliant
            ? coordinate.X
            : coordinate.Y;

        if (latitude < -MaxWebMercatorLatitude || latitude > MaxWebMercatorLatitude)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate),
                coordinate,
                $"Latitude {latitude} is outside the valid Web Mercator domain [-{MaxWebMercatorLatitude}, {MaxWebMercatorLatitude}].");
        }

        var longitudeRadians = DegreesToRadians(longitude);
        var latitudeRadians = DegreesToRadians(latitude);
        var x = EarthRadius * longitudeRadians;
        var y = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + (latitudeRadians / 2d)));

        return new GisCoordinate(x, y, coordinate.Z, coordinate.M);
    }

    private static GisCoordinate InverseWebMercator(
        GisCoordinate coordinate,
        GisAxisOrderPolicy axisOrderPolicy)
    {
        var longitude = RadiansToDegrees(coordinate.X / EarthRadius);
        var latitude = RadiansToDegrees((2d * Math.Atan(Math.Exp(coordinate.Y / EarthRadius))) - (Math.PI / 2d));

        return axisOrderPolicy == GisAxisOrderPolicy.AuthorityCompliant
            ? new GisCoordinate(latitude, longitude, coordinate.Z, coordinate.M)
            : new GisCoordinate(longitude, latitude, coordinate.Z, coordinate.M);
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180d;

    private static double RadiansToDegrees(double value) => value * 180d / Math.PI;
}
