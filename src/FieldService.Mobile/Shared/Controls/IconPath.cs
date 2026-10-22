using System.Globalization;

namespace FieldService.Mobile.Shared.Controls;

public static class IconPath
{
    private const double MaximumArcSegment = Math.PI / 2;

    public static PathF Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var reader = new Reader(data);
        var path = new PathF();
        var current = PointF.Zero;
        var start = PointF.Zero;
        var lastCubicControl = (PointF?)null;
        var lastQuadControl = (PointF?)null;
        var command = '\0';

        while (reader.SkipSeparators())
        {
            if (reader.PeekCommand() is { } next)
            {
                command = next;
                reader.Advance();
            }
            else if (command is '\0' or 'Z' or 'z')
            {
                throw new FormatException("Path data must start with a move command.");
            }

            var relative = char.IsLower(command);
            var origin = relative ? current : PointF.Zero;
            PointF? cubicControl = null;
            PointF? quadControl = null;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    current = Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    start = current;
                    path.MoveTo(current);
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    current = Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    path.LineTo(current);
                    break;
                case 'H':
                    current = new PointF(relative ? current.X + reader.ReadNumber() : reader.ReadNumber(), current.Y);
                    path.LineTo(current);
                    break;
                case 'V':
                    current = new PointF(current.X, relative ? current.Y + reader.ReadNumber() : reader.ReadNumber());
                    path.LineTo(current);
                    break;
                case 'C':
                case 'S':
                    var first = char.ToUpperInvariant(command) == 'S'
                        ? lastCubicControl is { } previousCubic ? Reflect(previousCubic, current) : current
                        : Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    var second = Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    current = Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    path.CurveTo(first, second, current);
                    cubicControl = second;
                    break;
                case 'Q':
                case 'T':
                    var control = char.ToUpperInvariant(command) == 'T'
                        ? lastQuadControl is { } previousQuad ? Reflect(previousQuad, current) : current
                        : Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    current = Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    path.QuadTo(control, current);
                    quadControl = control;
                    break;
                case 'A':
                    var radiusX = reader.ReadNumber();
                    var radiusY = reader.ReadNumber();
                    var rotation = reader.ReadNumber();
                    var largeArc = reader.ReadFlag();
                    var sweep = reader.ReadFlag();
                    var end = Offset(origin, reader.ReadNumber(), reader.ReadNumber());
                    AddArc(path, current, end, radiusX, radiusY, rotation, largeArc, sweep);
                    current = end;
                    break;
                case 'Z':
                    path.Close();
                    current = start;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{command}'.");
            }

            lastCubicControl = cubicControl;
            lastQuadControl = quadControl;
        }

        return path;
    }

    private static PointF Offset(PointF origin, float x, float y) => new(origin.X + x, origin.Y + y);

    private static PointF Reflect(PointF control, PointF about) => new((2 * about.X) - control.X, (2 * about.Y) - control.Y);

    private static void AddArc(PathF path, PointF from, PointF to, float radiusX, float radiusY, float rotationDegrees, bool largeArc, bool sweep)
    {
        if (from == to)
        {
            return;
        }

        double rx = Math.Abs(radiusX);
        double ry = Math.Abs(radiusY);
        if (rx == 0 || ry == 0)
        {
            path.LineTo(to);
            return;
        }

        var phi = rotationDegrees * Math.PI / 180;
        var cos = Math.Cos(phi);
        var sin = Math.Sin(phi);
        var dx = (from.X - to.X) / 2.0;
        var dy = (from.Y - to.Y) / 2.0;
        var x1 = (cos * dx) + (sin * dy);
        var y1 = (-sin * dx) + (cos * dy);

        var lambda = (x1 * x1 / (rx * rx)) + (y1 * y1 / (ry * ry));
        if (lambda > 1)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        var numerator = (rx * rx * ry * ry) - (rx * rx * y1 * y1) - (ry * ry * x1 * x1);
        var denominator = (rx * rx * y1 * y1) + (ry * ry * x1 * x1);
        var factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (largeArc == sweep ? -1 : 1);
        var cx1 = factor * rx * y1 / ry;
        var cy1 = factor * -ry * x1 / rx;
        var cx = (cos * cx1) - (sin * cy1) + ((from.X + to.X) / 2.0);
        var cy = (sin * cx1) + (cos * cy1) + ((from.Y + to.Y) / 2.0);

        var startAngle = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        var delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        var segments = (int)Math.Ceiling(Math.Abs(delta) / MaximumArcSegment - 1e-9);
        segments = Math.Max(segments, 1);
        var step = delta / segments;
        var handle = 4.0 / 3.0 * Math.Tan(step / 4);

        for (var index = 0; index < segments; index++)
        {
            var theta1 = startAngle + (index * step);
            var theta2 = theta1 + step;
            var first = ArcPoint(cx, cy, rx, ry, cos, sin, theta1, handle);
            var second = ArcPoint(cx, cy, rx, ry, cos, sin, theta2, -handle);
            var end = index == segments - 1 ? to : ArcPoint(cx, cy, rx, ry, cos, sin, theta2, 0);
            path.CurveTo(first, second, end);
        }
    }

    private static PointF ArcPoint(double cx, double cy, double rx, double ry, double cos, double sin, double theta, double tangent)
    {
        var x = Math.Cos(theta) - (tangent * Math.Sin(theta));
        var y = Math.Sin(theta) + (tangent * Math.Cos(theta));
        var px = rx * x;
        var py = ry * y;
        return new PointF((float)((cos * px) - (sin * py) + cx), (float)((sin * px) + (cos * py) + cy));
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        var dot = (ux * vx) + (uy * vy);
        var length = Math.Sqrt((ux * ux) + (uy * uy)) * Math.Sqrt((vx * vx) + (vy * vy));
        var angle = Math.Acos(Math.Clamp(dot / length, -1, 1));
        return (ux * vy) - (uy * vx) < 0 ? -angle : angle;
    }

    private sealed class Reader(string data)
    {
        private int _position;

        public bool SkipSeparators()
        {
            while (_position < data.Length && (char.IsWhiteSpace(data[_position]) || data[_position] == ','))
            {
                _position++;
            }

            return _position < data.Length;
        }

        public char? PeekCommand()
        {
            var next = data[_position];
            return char.IsAsciiLetter(next) && next is not ('e' or 'E') ? next : null;
        }

        public void Advance() => _position++;

        public bool ReadFlag()
        {
            SkipSeparators();
            if (_position < data.Length && data[_position] is '0' or '1')
            {
                return data[_position++] == '1';
            }

            throw new FormatException("An arc flag must be 0 or 1.");
        }

        public float ReadNumber()
        {
            SkipSeparators();
            var begin = _position;
            if (_position < data.Length && data[_position] is '+' or '-')
            {
                _position++;
            }

            var seenDot = false;
            var seenDigit = false;
            while (_position < data.Length)
            {
                var character = data[_position];
                if (char.IsAsciiDigit(character))
                {
                    seenDigit = true;
                }
                else if (character == '.' && !seenDot)
                {
                    seenDot = true;
                }
                else
                {
                    break;
                }

                _position++;
            }

            if (_position < data.Length && data[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < data.Length && data[_position] is '+' or '-')
                {
                    _position++;
                }

                while (_position < data.Length && char.IsAsciiDigit(data[_position]))
                {
                    _position++;
                }
            }

            if (!seenDigit)
            {
                throw new FormatException($"A number was expected at position {begin}.");
            }

            return float.Parse(data.AsSpan(begin, _position - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
