using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

enum TileType
{
    WALL = 0,
    FLOOR = 1,
    WATER = 2,
    DRUG = 3,
    VIRUS = 4,
}

public class Level : MonoBehaviour
{
    // fields/variables you may adjust from Unity's interface
    public int width = 16;   // size of level (default 16 x 16 blocks)
    public int length = 16;
    public float storey_height = 2.5f;   // height of walls
    public float virus_speed = 3.0f;     // virus velocity
    public GameObject fps_prefab;        // these should be set to prefabs as provided in the starter scene
    public GameObject virus_prefab;
    public GameObject water_prefab;
    public GameObject house_prefab;
    public GameObject text_box;
    public GameObject scroll_bar;
    public RetryMenu retry_menu;
    public AudioClip Start_sound;
    public AudioClip Hit_sound;
    public AudioClip Damage_sound;
    public AudioClip Death_sound;
    public AudioClip Pool_sound;
    public AudioClip Drug_pickup_sound;
    public AudioClip Victory_sound;
    public AudioSource Audio_source;

    private float nextSoundTime = 0.0f;
    private bool played_end = false;

    // fields/variables accessible from other scripts
    internal GameObject fps_player_obj;   // instance of FPS template
    internal float player_health = 1.0f;  // player health in range [0.0, 1.0]
    internal int num_virus_hit_concurrently = 0;            // how many viruses hit the player before washing them off
    internal bool virus_landed_on_player_recently = false;  // has virus hit the player? if yes, a timer of 5sec starts before infection
    internal float timestamp_virus_landed = float.MaxValue; // timestamp to check how many sec passed since the virus landed on player
    internal bool drug_landed_on_player_recently = false;   // has drug collided with player?
    internal bool player_is_on_water = false;               // is player on water block
    internal bool player_entered_house = false;             // has player arrived in house?

    // fields/variables needed only from this script
    private Bounds bounds;                   // size of ground plane in world space coordinates 
    private float timestamp_last_msg = 0.0f; // timestamp used to record when last message on GUI happened (after 7 sec, default msg appears)
    private int function_calls = 0;          // number of function calls during backtracking for solving the CSP
    private int num_viruses = 0;             // number of viruses in the level
    private List<int[]> pos_viruses;         // stores their location in the grid
    private List<TileType>[,] grid_copy;     // stores a copy of the procedurally generated maze, for the purpose of retrying the level
    private int[] start_pos;                 // stores a copy of the player starting position which is placed randomly at first

    // a helper function that randomly shuffles the elements of a list (useful to randomize the solution to the CSP)
    private void Shuffle<T>(ref List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Range(0, n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    // Use this for initialization
    void Start()
    {
        // initialize internal/private variables
        bounds = GetComponent<Collider>().bounds; 
        timestamp_last_msg = 0.0f;
        function_calls = 0;
        num_viruses = 0;
        player_health = 1.0f;
        num_virus_hit_concurrently = 0;
        virus_landed_on_player_recently = false;
        timestamp_virus_landed = float.MaxValue;
        drug_landed_on_player_recently = false;
        player_is_on_water = false;
        player_entered_house = false;        

        // initialize 2D grid
        List<TileType>[,] grid = new List<TileType>[width, length];
        // useful to keep variables that are unassigned so far
        List<int[]> unassigned = new List<int[]>();

        // will place x viruses in the beginning (at least 1). x depends on the sise of the grid (the bigger, the more viruses)        
        num_viruses = width * length / 25 + 1; // at least one virus will be added
        pos_viruses = new List<int[]>();
        // create the wall perimeter of the level, and let the interior as unassigned
        // then try to assign variables to satisfy all constraints
        // *rarely* it might be impossible to satisfy all constraints due to initialization
        // in this case of no success, we'll restart the random initialization and try to re-solve the CSP
        bool success = false;
        while (!success)
        {
            for (int v = 0; v < num_viruses; v++)
            {
                while (true) // try until virus placement is successful (unlikely that there will no places)
                {
                    // try a random location in the grid
                    int wr = Random.Range(1, width - 1);
                    int lr = Random.Range(1, length - 1);

                    // if grid location is empty/free, place it there
                    if (grid[wr, lr] == null)
                    {
                        grid[wr, lr] = new List<TileType> { TileType.VIRUS };
                        pos_viruses.Add(new int[2] { wr, lr });
                        break;
                    }
                }
            }

            for (int w = 0; w < width; w++)
                for (int l = 0; l < length; l++)
                    if (w == 0 || l == 0 || w == width - 1 || l == length - 1)
                        grid[w, l] = new List<TileType> { TileType.WALL };
                    else
                    {
                        if (grid[w, l] == null) // does not have virus already or some other assignment from previous run
                        {
                            // CSP will involve assigning variables to one of the following four values (VIRUS is predefined for some tiles)
                            List<TileType> candidate_assignments = new List<TileType> { TileType.WALL, TileType.FLOOR, TileType.WATER, TileType.DRUG };
                            Shuffle<TileType>(ref candidate_assignments);

                            grid[w, l] = candidate_assignments;
                            unassigned.Add(new int[] { w, l });
                        }
                    }

            Shuffle<int[]>(ref unassigned);
            success = BackTrackingSearch(grid, unassigned);
            if (!success)
            {
                Debug.Log("Could not find valid solution - will try again");
                unassigned.Clear();
                grid = new List<TileType>[width, length];
                function_calls = 0; 
            }
        }

        grid_copy = grid;
        DrawDungeon(grid);
        Audio_source.PlayOneShot(Start_sound);
    }

    bool DoWeHaveTooManyInteriorWallsORWaterORDrug(List<TileType>[,] grid)
    {
        int[] number_of_assigned_elements = new int[] { 0, 0, 0, 0, 0 };
        for (int w = 0; w < width; w++)
            for (int l = 0; l < length; l++)
            {
                if (w == 0 || l == 0 || w == width - 1 || l == length - 1)
                    continue;
                if (grid[w, l].Count == 1)
                    number_of_assigned_elements[(int)grid[w, l][0]]++;
            }

        if ((number_of_assigned_elements[(int)TileType.WALL] > num_viruses * 10) ||
             (number_of_assigned_elements[(int)TileType.WATER] > (width + length) / 4) ||
             (number_of_assigned_elements[(int)TileType.DRUG] >= num_viruses / 2))
            return true;
        else
            return false;
    }

    bool DoWeHaveTooFewWallsORWaterORDrug(List<TileType>[,] grid)
    {
        int[] number_of_potential_assignments = new int[] { 0, 0, 0, 0, 0 };
        for (int w = 0; w < width; w++)
            for (int l = 0; l < length; l++)
            {
                if (w == 0 || l == 0 || w == width - 1 || l == length - 1)
                    continue;
                for (int i = 0; i < grid[w, l].Count; i++)
                    number_of_potential_assignments[(int)grid[w, l][i]]++;
            }

        if ((number_of_potential_assignments[(int)TileType.WALL] < (width * length) / 4) ||
             (number_of_potential_assignments[(int)TileType.WATER] < num_viruses / 4) ||
             (number_of_potential_assignments[(int)TileType.DRUG] < num_viruses / 4))
            return true;
        else
            return false;
    }

    // must return true if there are three (or more) interior consecutive wall blocks either horizontally or vertically
    bool TooLongWall(List<TileType>[,] grid)
    {
        for (var i = 1; i < width; i++)
        {
            // Traverse each row
            int last_empty = 0; // Denotes last found "non-wall" tile
            for (var j = 1; j < length; j++)
            {
                // If the current tile is not a wall
                if (grid[i, j].Count != 1 || grid[i, j][0] != TileType.WALL)
                {
                    // If the distance between the last "non-wall" tile, and this one is greater than 3
                    if (j - last_empty >= 3)
                        return true;
                    last_empty = j;
                }
            }
        }
        for (var i = 1; i < width; i++)
        {
            // Traverse each row
            int last_empty = 0; // Denotes last found "non-wall" tile
            for (var j = 1; j < length; j++)
            {
                // If the current tile is not a wall
                if (grid[j, i].Count != 1 || grid[j, i][0] != TileType.WALL)
                {
                    // If the distance between the last "non-wall" tile, and this one is greater than 3
                    if (j - last_empty >= 3)
                        return true;
                    last_empty = j;
                }
            }
        }
        return false;
    }

    // must return true if there is no WALL adjacent to a virus 
    bool NoWallsCloseToVirus(List<TileType>[,] grid)
    {
        foreach (var pos in pos_viruses)
        {
            bool flag = false; // Assume that the virus is not close to any wall
            // Iterate through the neighbouring points of the virus
            for (var i = -1; i <= 1; i++)
            {
                for (var j = -1; j <= 1; j++)
                {
                    // If an adjacent point is a potential/fixed wall
                    if (!(i == 0 && j == 0) && grid[pos[0] + i, pos[1] + j].Contains(TileType.WALL))
                    {
                        flag = true;
                        break;
                    }
                }
                if (flag) break;
            }
            if (!flag)
                return true;
        }
        return false;
    }


    // check if attempted assignment is consistent with the constraints or not
    bool CheckConsistency(List<TileType>[,] grid, int[] cell_pos, TileType t)
    {
        int w = cell_pos[0];
        int l = cell_pos[1];

        List<TileType> old_assignment = new List<TileType>();
        old_assignment.AddRange(grid[w, l]);
        grid[w, l] = new List<TileType> { t };

		// note that we negate the functions here i.e., check if we are consistent with the constraints we want
        bool areWeConsistent = !DoWeHaveTooFewWallsORWaterORDrug(grid) && !DoWeHaveTooManyInteriorWallsORWaterORDrug(grid) 
                            && !TooLongWall(grid) && !NoWallsCloseToVirus(grid);

        grid[w, l] = new List<TileType>();
        grid[w, l].AddRange(old_assignment);
        return areWeConsistent;
    }

    // backtracking 
    bool BackTrackingSearch(List<TileType>[,] grid, List<int[]> unassigned)
    {
        // if there are too many recursive function evaluations, then backtracking has become too slow (or constraints cannot be satisfied)
        // to provide a reasonable amount of time to start the level, we put a limit on the total number of recursive calls
        // if the number of calls exceed the limit, then it's better to try a different initialization
        if (function_calls++ > 100000)       
            return false;

        // we are done!
        // Was not able to find perfect solutions, which led memory stack overflow due to this function being called infinite times
        // Thus, a low limit of unassigned squares is set in order to get an approximate solution
        if (unassigned.Count < 5)
            return true;

        int[] pos = unassigned[0]; // Get an unassigned node

        // Try all possible assignments for this node
        for (int i = 0; i < grid[pos[0], pos[1]].Count; i++)
        {
            TileType t = grid[pos[0], pos[1]][i];
            // If the assignment is consistent with all constraints
            if (CheckConsistency(grid, pos, t))
            {
                List<TileType> old_assignment = new List<TileType>();
                old_assignment.AddRange(grid[pos[0], pos[1]]);
                grid[pos[0], pos[1]] = new List<TileType> { t };
                unassigned.RemoveAt(0);

                // Search for a solution with this assignment
                if (BackTrackingSearch(grid, unassigned)) return true;

                // Backtrack incase of failure
                grid[pos[0], pos[1]] = new List<TileType>();
                grid[pos[0], pos[1]].AddRange(old_assignment);
                unassigned.Insert(0, pos);
            }
        }

        // No assignment is valid
        return false;
    }


    // places the primitives/objects according to the grid assignents
    void DrawDungeon(List<TileType>[,] solution)
    {
        GetComponent<Renderer>().material.color = Color.grey; // ground plane will be grey

        // place character at random position (wr, lr) in terms of grid coordinates (integers)
        // make sure that this random position is a FLOOR tile (not wall, drug, or virus)
        int wr = 0;
        int lr = 0;
        while (true) // try until a valid position is sampled
        {
            wr = Random.Range(1, width - 1);
            lr = Random.Range(1, length - 1);

            if (solution[wr, lr][0] == TileType.FLOOR)
            {
                float x = bounds.min[0] + (float)wr * (bounds.size[0] / (float)width);
                float z = bounds.min[2] + (float)lr * (bounds.size[2] / (float)length);
                fps_player_obj = Instantiate(fps_prefab);
                fps_player_obj.name = "PLAYER";
                // character is placed above the level so that in the beginning, he appears to fall down onto the maze
                fps_player_obj.transform.position = new Vector3(x + 0.5f, 1.0f * storey_height, z + 0.5f);
                break;
            }
        }
        start_pos = new int[] { wr, lr };

        // place an exit from the maze at location (wee, lee) in terms of grid coordinates (integers)
        // destroy the wall segment there - the grid will be used to place a house
        // the exist will be placed as far as away from the character (yet, with some randomness, so that it's not always located at the corners)
        int max_dist = -1;
        int wee = -1;
        int lee = -1;
        while (true) // try until a valid position is sampled
        {
            if (wee != -1)
                break;
            for (int we = 0; we < width; we++)
            {
                for (int le = 0; le < length; le++)
                {
                    // skip corners
                    if (we == 0 && le == 0)
                        continue;
                    if (we == 0 && le == length - 1)
                        continue;
                    if (we == width - 1 && le == 0)
                        continue;
                    if (we == width - 1 && le == length - 1)
                        continue;

                    if (we == 0 || le == 0 || wee == length - 1 || lee == length - 1)
                    {
                        // randomize selection
                        if (Random.Range(0.0f, 1.0f) < 0.1f)
                        {
                            int dist = System.Math.Abs(wr - we) + System.Math.Abs(lr - le);
                            if (dist > max_dist) // must be placed far away from the player
                            {
                                wee = we;
                                lee = le;
                                max_dist = dist;
                            }
                        }
                    }
                }
            }
        }


        // checks whether all paths between the player at (wr,lr) and the exit (wee, lee) are blocked by walls

        // A modified Dijktra Search
        // =========================
        // Rather than finding the shortest path, it finds the path which needs the 
        // least number of walls to be broken
        // A dictionary is used as a priority queue, where the priority of each grid point is stored.
        // Priority : Number of wall-breaks needed

        Dictionary<int[], int> pq = new Dictionary<int[], int>(new ArrayEqualityComparer());
        int[,][] parent = new int[16, 16][]; // A 2d array to keep track of the path traversed by the
                                             // Dijkstra algorithm
        for (int i = 1; i < width-1; i++)
            for (int j = 1; j < length-1; j++)
                pq.Add(new int[] { i, j }, 999); // Set an initial high priority for each node

        pq[new int[] { wr, lr }] = 0; // The starting node is set to 0 priority so that it is picked first
        parent[wr, lr] = null; // The parent of the starting node is null

        // Not considering diagonals for this dijkstra search because such a path found,
        // which considers diagonal movement, may not be traversable by the player
        List<int[]> neighbourhood = new List<int[]> { 
            new int[] { -1, 0 },
            new int[] { 0, -1 },
            new int[] { 0, 1 },
            new int[] { 1, 0 },
        };
        int[] end = null; // To store the location of the last node traversed

        while (pq.Count > 0)
        {
            int[] head = new int[] { };
            int min_p = 1000;
            // Finding the node with the minimum priority, in the priority queue
            foreach (var item in pq)
            {
                if(item.Value <= min_p)
                {
                    min_p = item.Value;
                    head = item.Key;
                }
            }
            pq.Remove(head); // Remove the least priority node

            // Update the priority of the neighbouring nodes
            foreach (var n in neighbourhood)
            {
                int[] pos = new int[] { head[0] + n[0], head[1] + n[1] };
                // If the destination is reached
                if (pos[0] == wee && pos[1] == lee)
                {
                    end = head;
                    break;
                }

                if (pq.ContainsKey(pos))
                {
                    int temp = 0;
                    // If the neighbour is a wall, we will have to break it in order to traverse it
                    if (solution[pos[0], pos[1]][0] == TileType.WALL) temp = 1;

                    // Update the cost of the neighbour
                    if (min_p + temp < pq[pos])
                    {
                        pq[pos] = Mathf.Min(pq[pos], min_p + temp);
                        parent[pos[0], pos[1]] = head;
                    }
                }
            }
            if (end != null)
                break;
        }
        
        // Retrace the path followed by the Dijkstra algorithm, removing each wall encountered
        int[] p = end;
        while (p != null)
        {
            if (solution[p[0], p[1]][0] == TileType.WALL)
                solution[p[0], p[1]][0] = TileType.FLOOR;

            p = parent[p[0], p[1]];
        }

        int w = 0;
        for (float x = bounds.min[0]; x < bounds.max[0]; x += bounds.size[0] / (float)width - 1e-6f, w++)
        {
            int l = 0;
            for (float z = bounds.min[2]; z < bounds.max[2]; z += bounds.size[2] / (float)length - 1e-6f, l++)
            {
                if ((w >= width) || (l >= width))
                    continue;

                float y = bounds.min[1];
                //Debug.Log(w + " " + l + " " + h);
                if ((w == wee) && (l == lee)) // this is the exit
                {
                    GameObject house = Instantiate(house_prefab, new Vector3(0, 0, 0), Quaternion.identity);
                    house.name = "HOUSE";
                    house.transform.position = new Vector3(x + 0.5f, y, z + 0.5f);
                    if (l == 0)
                        house.transform.Rotate(0.0f, 270.0f, 0.0f);
                    else if (w == 0)
                        house.transform.Rotate(0.0f, 0.0f, 0.0f);
                    else if (l == length - 1)
                        house.transform.Rotate(0.0f, 90.0f, 0.0f);
                    else if (w == width - 1)
                        house.transform.Rotate(0.0f, 180.0f, 0.0f);

                    house.AddComponent<BoxCollider>();
                    house.GetComponent<BoxCollider>().isTrigger = true;
                    house.GetComponent<BoxCollider>().size = new Vector3(3.0f, 3.0f, 3.0f);
                    house.AddComponent<House>();
                }
                else if (solution[w, l][0] == TileType.WALL)
                {
                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "WALL";
                    cube.transform.localScale = new Vector3(bounds.size[0] / (float)width, storey_height, bounds.size[2] / (float)length);
                    cube.transform.position = new Vector3(x + 0.5f, y + storey_height / 2.0f, z + 0.5f);
                    cube.GetComponent<Renderer>().material.color = new Color(0.6f, 0.8f, 0.8f);
                }
                else if (solution[w, l][0] == TileType.VIRUS)
                {
                    GameObject virus = Instantiate(virus_prefab, new Vector3(0, 0, 0), Quaternion.identity);
                    virus.name = "COVID";
                    virus.transform.position = new Vector3(x + 0.5f, y + Random.Range(1.0f, storey_height / 2.0f), z + 0.5f);

                    //GameObject virus = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    //virus.GetComponent<Renderer>().material.color = new Color(0.5f, 0.0f, 0.0f);
                    //virus.name = "ENEMY";
                    //virus.transform.position = new Vector3(x + 0.5f, y + Random.Range(1.0f, storey_height / 2.0f), z + 0.5f);
                    //virus.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
                    //virus.AddComponent<BoxCollider>();
                    //virus.GetComponent<BoxCollider>().size = new Vector3(1.2f, 1.2f, 1.2f);
                    //virus.AddComponent<Rigidbody>();
                    //virus.GetComponent<Rigidbody>().useGravity = false;

                    virus.AddComponent<Virus>();
                    virus.GetComponent<Rigidbody>().mass = 10000;
                    virus.gameObject.tag = "ToReset";
                }
                else if (solution[w, l][0] == TileType.DRUG)
                {
                    GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    capsule.name = "DRUG";
                    capsule.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
                    capsule.transform.position = new Vector3(x + 0.5f, y + Random.Range(1.0f, storey_height / 2.0f), z + 0.5f);
                    capsule.GetComponent<Renderer>().material.color = Color.green;
                    capsule.AddComponent<Drug>();
                    capsule.gameObject.tag = "ToReset";
                }
                else if (solution[w, l][0] == TileType.WATER)
                {
                    GameObject water = Instantiate(water_prefab, new Vector3(0, 0, 0), Quaternion.identity);
                    water.name = "WATER";
                    water.transform.localScale = new Vector3(0.5f * bounds.size[0] / (float)width, 1.0f, 0.5f * bounds.size[2] / (float)length);
                    water.transform.position = new Vector3(x + 0.5f, y + 0.1f, z + 0.5f);

                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "WATER_BOX";
                    cube.transform.localScale = new Vector3(bounds.size[0] / (float)width, storey_height / 20.0f, bounds.size[2] / (float)length);
                    cube.transform.position = new Vector3(x + 0.5f, y, z + 0.5f);
                    cube.GetComponent<Renderer>().material.color = Color.grey;
                    cube.GetComponent<BoxCollider>().size = new Vector3(1.1f, 20.0f * storey_height, 1.1f);
                    cube.GetComponent<BoxCollider>().isTrigger = true;
                    cube.AddComponent<Water>();
                }
            }
        }
    }


    void Update()
    {
        if (player_health < 0.001f) // the player dies here
        {
            text_box.GetComponent<Text>().text = "Failed!";

            if (fps_player_obj != null)
            {
                GameObject grave = GameObject.CreatePrimitive(PrimitiveType.Cube);
                grave.name = "GRAVE";
                grave.transform.localScale = new Vector3(bounds.size[0] / (float)width, 2.0f * storey_height, bounds.size[2] / (float)length);
                grave.transform.position = fps_player_obj.transform.position;
                grave.GetComponent<Renderer>().material.color = Color.black;
                grave.gameObject.tag = "ToReset";

                Object.Destroy(fps_player_obj);
                Camera.main.GetComponent<AudioListener>().enabled = true;
                if (!played_end)
                {
                    played_end = true;
                    Audio_source.PlayOneShot(Death_sound);
                    nextSoundTime = Time.time + 2.0f;
                }
                StartCoroutine(wait_Death());
            }
            return;
        }
        if (player_entered_house) // the player suceeds here, variable manipulated by House.cs
        {
            if (virus_landed_on_player_recently)
                text_box.GetComponent<Text>().text = "Washed it off at home! Success!!!";
            else
                text_box.GetComponent<Text>().text = "Success!!!";

            Object.Destroy(fps_player_obj);
            Camera.main.GetComponent<AudioListener>().enabled = true;
            if (!played_end)
            {
                played_end = true;
                Audio_source.PlayOneShot(Victory_sound);
                nextSoundTime = Time.time + 2.0f;
            }
            StartCoroutine(wait_Victory());
            return;
        }

        if (Time.time - timestamp_last_msg > 7.0f) // renew the msg by restating the initial goal
        {
            text_box.GetComponent<Text>().text = "Find your home!";
        }

        // virus hits the players (boolean variable is manipulated by Virus.cs)
        if (virus_landed_on_player_recently)
        {
            float time_since_virus_landed = Time.time - timestamp_virus_landed;
            if (time_since_virus_landed > 5.0f)
            {
                player_health -= Random.Range(0.25f, 0.5f) * (float)num_virus_hit_concurrently;
                player_health = Mathf.Max(player_health, 0.0f);
                if (num_virus_hit_concurrently > 1)
                    text_box.GetComponent<Text>().text = "Ouch! Infected by " + num_virus_hit_concurrently + " viruses";
                else
                    text_box.GetComponent<Text>().text = "Ouch! Infected by a virus";
                timestamp_last_msg = Time.time;
                timestamp_virus_landed = float.MaxValue;
                virus_landed_on_player_recently = false;
                num_virus_hit_concurrently = 0;
                if (Time.time >= nextSoundTime)
                {
                    Audio_source.PlayOneShot(Damage_sound);
                    nextSoundTime = Time.time + 2.0f;
                }
            }
            else
            {
                if (num_virus_hit_concurrently == 1)
                    text_box.GetComponent<Text>().text = "A virus landed on you. Infection in " + (5.0f - time_since_virus_landed).ToString("0.0") + " seconds. Find water or drug!";
                else
                    text_box.GetComponent<Text>().text = num_virus_hit_concurrently + " viruses landed on you. Infection in " + (5.0f - time_since_virus_landed).ToString("0.0") + " seconds. Find water or drug!";
                if (time_since_virus_landed < 0.5f && Time.time >= nextSoundTime)
                {
                    Audio_source.PlayOneShot(Hit_sound);
                    nextSoundTime = Time.time + 2.0f;
                }
            }
        }

        // drug picked by the player  (boolean variable is manipulated by Drug.cs)
        if (drug_landed_on_player_recently)
        {
            if (player_health < 0.999f || virus_landed_on_player_recently)
                text_box.GetComponent<Text>().text = "Phew! New drug helped!";
            else
                text_box.GetComponent<Text>().text = "No drug was needed!";
            timestamp_last_msg = Time.time;
            player_health += Random.Range(0.25f, 0.75f);
            player_health = Mathf.Min(player_health, 1.0f);
            drug_landed_on_player_recently = false;
            timestamp_virus_landed = float.MaxValue;
            virus_landed_on_player_recently = false;
            num_virus_hit_concurrently = 0;
            if (Time.time >= nextSoundTime)
            {
                Audio_source.PlayOneShot(Drug_pickup_sound);
                nextSoundTime = Time.time + 2.0f;
            }
        }

        // splashed on water  (boolean variable is manipulated by Water.cs)
        if (player_is_on_water)
        {
            if (virus_landed_on_player_recently)
                text_box.GetComponent<Text>().text = "Phew! Washed it off!";
            timestamp_last_msg = Time.time;
            timestamp_virus_landed = float.MaxValue;
            virus_landed_on_player_recently = false;
            num_virus_hit_concurrently = 0;
            if (Time.time >= nextSoundTime)
            {
                Audio_source.PlayOneShot(Pool_sound);
                nextSoundTime = Time.time + 2.0f;
            }
        }

        // update scroll bar (not a very conventional manner to create a health bar, but whatever)
        scroll_bar.GetComponent<Scrollbar>().size = player_health;
        if (player_health < 0.5f)
        {
            ColorBlock cb = scroll_bar.GetComponent<Scrollbar>().colors;
            cb.disabledColor = new Color(1.0f, 0.0f, 0.0f);
            scroll_bar.GetComponent<Scrollbar>().colors = cb;
        }
        else
        {
            ColorBlock cb = scroll_bar.GetComponent<Scrollbar>().colors;
            cb.disabledColor = new Color(0.0f, 1.0f, 0.25f);
            scroll_bar.GetComponent<Scrollbar>().colors = cb;
        }

    }

    public void RetryLevel()
    {
        Camera.main.GetComponent<AudioListener>().enabled = false;
        played_end = false;

        GameObject[] obs_to_reset = GameObject.FindGameObjectsWithTag("ToReset");
        foreach (GameObject obj in obs_to_reset)
            Destroy(obj);

        float x = bounds.min[0] + (float)start_pos[0] * (bounds.size[0] / (float)width);
        float z = bounds.min[2] + (float)start_pos[1] * (bounds.size[2] / (float)length);
        fps_player_obj = Instantiate(fps_prefab);
        fps_player_obj.name = "PLAYER";
        // character is placed above the level so that in the beginning, he appears to fall down onto the maze
        fps_player_obj.transform.position = new Vector3(x + 0.5f, 1.0f * storey_height, z + 0.5f);
        player_health = 1.0f;

        int w = 0;
        for (x = bounds.min[0]; x < bounds.max[0]; x += bounds.size[0] / (float)width - 1e-6f, w++)
        {
            int l = 0;
            for (z = bounds.min[2]; z < bounds.max[2]; z += bounds.size[2] / (float)length - 1e-6f, l++)
            {
                if ((w >= width) || (l >= width))
                    continue;

                float y = bounds.min[1];
                if (grid_copy[w, l][0] == TileType.VIRUS)
                {
                    GameObject virus = Instantiate(virus_prefab, new Vector3(0, 0, 0), Quaternion.identity);
                    virus.name = "COVID";
                    virus.transform.position = new Vector3(x + 0.5f, y + Random.Range(1.0f, storey_height / 2.0f), z + 0.5f);
                    virus.AddComponent<Virus>();
                    virus.GetComponent<Rigidbody>().mass = 10000;
                }
                else if (grid_copy[w, l][0] == TileType.DRUG)
                {
                    GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    capsule.name = "DRUG";
                    capsule.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
                    capsule.transform.position = new Vector3(x + 0.5f, y + Random.Range(1.0f, storey_height / 2.0f), z + 0.5f);
                    capsule.GetComponent<Renderer>().material.color = Color.green;
                    capsule.AddComponent<Drug>();
                }
            }
        }
        Audio_source.PlayOneShot(Start_sound);
    }

    IEnumerator wait_Victory()
    {
        yield return new WaitForSeconds(3);
        SceneManager.LoadScene("Victory");
    }

    IEnumerator wait_Death()
    {
        yield return new WaitForSeconds(3);
        retry_menu.ShowMenu();
    }
}

public class ArrayEqualityComparer : IEqualityComparer<int[]>
{
    public bool Equals(int[] x, int[] y)
    {
        if (x.Length != y.Length)
        {
            return false;
        }
        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] != y[i])
            {
                return false;
            }
        }
        return true;
    }

    public int GetHashCode(int[] obj)
    {
        int result = 17;
        for (int i = 0; i < obj.Length; i++)
        {
            unchecked
            {
                result = result * 23 + obj[i];
            }
        }
        return result;
    }
}