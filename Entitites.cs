namespace Entities
{
    public class Superhero
    {
        public int SuperheroId { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; }
        public string Gender { get; set; }
        public string Alignment { get; set; }
        public string Universe { get; set; }
    }

    public class Superpower
    {
        public int PowerId { get; set; }
        public string Name { get; set; }
    }

    public class SuperheroPower
    {
        public int SuperheroId { get; set; }
        public int PowerId { get; set; }
    }

    public class Team
    {
        public int TeamId { get; set; }
        public string Name { get; set; }
    }

    public class SuperheroTeam
    {
        public int SuperheroId { get; set; }
        public int TeamId { get; set; }
    }
}
