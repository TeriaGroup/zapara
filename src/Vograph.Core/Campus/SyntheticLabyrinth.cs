namespace Vograph.Core.Campus;

public static class SyntheticLabyrinth
{
    public static CampusGraph Build() => CampusGraph.Load("""
        {
          "version": 1,
          "buildings": ["УЛК"],
          "nodes": [
            {"id":"lab.room.west.3","kind":"room","building":"УЛК","floor":3,"x":0.2,"y":0.2,"room":"W3"},
            {"id":"lab.stair.west.3","kind":"stair","building":"УЛК","floor":3,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.stair.west.2","kind":"stair","building":"УЛК","floor":2,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.stair.west.1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.room.east.3","kind":"room","building":"УЛК","floor":3,"x":0.8,"y":0.2,"room":"E3"},
            {"id":"lab.stair.east.3","kind":"stair","building":"УЛК","floor":3,"x":0.8,"y":0.5,"group":"lab.stair.east"},
            {"id":"lab.stair.east.2","kind":"stair","building":"УЛК","floor":2,"x":0.8,"y":0.5,"group":"lab.stair.east"},
            {"id":"lab.stair.east.1","kind":"stair","building":"УЛК","floor":1,"x":0.8,"y":0.5,"group":"lab.stair.east"}
          ],
          "edges": [
            {"from":"lab.room.west.3","to":"lab.stair.west.3","kind":"walk","seconds":10,"oneWay":false},
            {"from":"lab.room.east.3","to":"lab.stair.east.3","kind":"walk","seconds":10,"oneWay":false},
            {"from":"lab.stair.west.3","to":"lab.stair.west.2","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.2","to":"lab.stair.west.1","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.1","to":"lab.stair.west.2","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.2","to":"lab.stair.west.3","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.3","to":"lab.stair.east.2","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.2","to":"lab.stair.east.1","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.1","to":"lab.stair.east.2","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.2","to":"lab.stair.east.3","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.1","to":"lab.stair.east.1","kind":"walk","seconds":30,"oneWay":false,"points":[[0.2,0.5],[0.5,0.5],[0.8,0.5]]}
          ]
        }
        """);
}
