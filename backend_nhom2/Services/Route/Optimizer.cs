using Google.OrTools.ConstraintSolver;
using System;
using System.Collections.Generic;

namespace backend_nhom2.Services.Route
{
    public static class Optimizer
    {
        public class TwNode
        {
            public long Start;         // giây tương đối tính từ lúc khởi hành (>=0)
            public long End;           // giây tương đối (>=Start)
            public int ServiceSeconds; // thời gian phục vụ tại điểm (giây)
        }

        /// <summary>
        /// 1 xe + time-window, dùng ma trận thời gian (giây).
        /// Node 0 là depot; 1..N-1 là các điểm.
        /// Các time-window PHẢI là GIÂY TƯƠNG ĐỐI so với giờ xuất phát.
        /// </summary>
        public static (int[] order, int totalSec) SolveSingleVehicleTW(
            int[,] travelTimeSec,
            TwNode[] tws,                 // độ dài = n-1 (ứng với node 1..N-1)
            int serviceSecondsDefault,    // nếu TwNode không set
            int horizonSec = 7 * 24 * 3600)
        {
            int n = travelTimeSec.GetLength(0);
            if (n < 2) return (new[] { 0 }, 0);

            var manager = new RoutingIndexManager(n, 1, 0);
            var routing = new RoutingModel(manager);

            int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
            {
                int fromNode = manager.IndexToNode(fromIndex);
                int toNode = manager.IndexToNode(toIndex);
                return travelTimeSec[fromNode, toNode];
            });

            routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);

            // Dimension thời gian: start=0 tại depot, horizon theo tham số
            routing.AddDimension(
                transitCallbackIndex,
                24 * 3600,           // waiting/slack
                horizonSec,          // horizon tính bằng giây
                true,                // *** start tại 0 ***
                "Time"
            );

            var timeDimension = routing.GetMutableDimension("Time");

            // Depot: 0..horizon
            var depotIdx = manager.NodeToIndex(0);
            timeDimension.CumulVar(depotIdx).SetRange(0, horizonSec);

            // Các điểm: đặt TW tương đối (đã clamp)
            for (int i = 1; i < n; i++)
            {
                var idx = manager.NodeToIndex(i);
                long start = 0;
                long end = horizonSec;

                var tw = tws[i - 1];
                if (tw != null)
                {
                    start = Math.Max(0, tw.Start);
                    end = Math.Max(start, Math.Min(horizonSec, tw.End));
                }

                timeDimension.CumulVar(idx).SetRange(start, end);
            }

            // Service time -> SlackVar tại mỗi node (trừ depot)
            for (int i = 1; i < n; i++)
            {
                int svc = serviceSecondsDefault;
                if (tws[i - 1] != null && tws[i - 1].ServiceSeconds > 0)
                    svc = tws[i - 1].ServiceSeconds;

                timeDimension.SlackVar(manager.NodeToIndex(i)).SetValue(Math.Max(0, svc));
            }

            var searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
            searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
            searchParameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
            searchParameters.TimeLimit = new Google.Protobuf.WellKnownTypes.Duration { Seconds = 5 };

            var solution = routing.SolveWithParameters(searchParameters);
            if (solution == null) return (new[] { 0 }, 0);

            var order = new List<int>();
            long index = routing.Start(0);
            while (!routing.IsEnd(index))
            {
                order.Add(manager.IndexToNode(index));
                index = solution.Value(routing.NextVar(index));
            }
            order.Add(manager.IndexToNode(index)); // end

            int totalSec = (int)solution.ObjectiveValue();
            return (order.ToArray(), totalSec);
        }
    }
}
