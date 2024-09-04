using GenerateQue.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GenerateQue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class QuestionController : ControllerBase
    {
        private readonly IDatabase _redisDb;

        public QuestionController(IDatabase redisDb)
        {
            _redisDb = redisDb;
            InitializeCounterAsync().Wait(); 
        }


        private async Task InitializeCounterAsync()
        {
            var exists = await _redisDb.KeyExistsAsync("question:counter");
            if (!exists)
            {
                await _redisDb.StringSetAsync("question:counter", 0);
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<QuestionClass>>> GetAllQuestions()
        {
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: "question:*").ToList();

            var questions = new List<QuestionClass>();
            Random random = new Random();
            

            foreach (var key in keys)
            {
                if (await _redisDb.KeyTypeAsync(key) == RedisType.String)
                {
                    var value = await _redisDb.StringGetAsync(key);
                    if (!string.IsNullOrEmpty(value))
                    {
                        try
                        {
                            var questionAnswer = JsonConvert.DeserializeObject<QuestionClass>(value);
                            if (questionAnswer != null &&
                                !string.IsNullOrWhiteSpace(questionAnswer.Question))
                            {
                                questions.Add(questionAnswer);
                            }
                        }
                        catch (JsonSerializationException ex)
                        {
                            Console.WriteLine($"Serialization issue: {ex.Message}");
                        }
                    }
                }
            }

            var filteredQuestions = questions.Where(q =>
                !string.IsNullOrWhiteSpace(q.Question) &&
                !string.IsNullOrWhiteSpace(q.Answer)).ToList();

            var randomQuestions = filteredQuestions.OrderBy(q => random.Next()).Take(5).ToList();

            if (!randomQuestions.Any())
            {
                return NotFound("No questions found.");
            }

            return Ok(randomQuestions);
        }


        [HttpGet("{id}")]
        public async Task<ActionResult<QuestionClass>> GetQuestion(int id)
        {
            var key = $"question:{id}";
            var json = await _redisDb.StringGetAsync(key);
            if (json.IsNullOrEmpty)
            {
                return NotFound("Question not found.");
            }

            var questionAnswer = JsonConvert.DeserializeObject<QuestionClass>(json);
            return Ok(questionAnswer);
        }

        [HttpPost]
        public async Task<IActionResult> CreateQuestion([FromBody] QuestionClass request)
        {
            if (request == null || string.IsNullOrEmpty(request.Question) || string.IsNullOrEmpty(request.Answer))
            {
                return BadRequest("Invalid question-answer object.");
            }

            var nextId = await _redisDb.StringIncrementAsync("question:counter");
            var key = $"question:{nextId}";

            request.Id = (int)nextId; 
            var json = JsonConvert.SerializeObject(request);
            await _redisDb.StringSetAsync(key, json);

            return CreatedAtAction(nameof(GetQuestion), new { id = request.Id }, request);
        }

        [HttpPost("validate")]
        public async Task<IActionResult> ValidateAnswer([FromBody] QuestionClass request)
        {
            if (request == null || request.Id <= 0 || string.IsNullOrEmpty(request.Answer))
            {
                return BadRequest("Invalid request.");
            }

            try
            {
                var key = $"question:{request.Id}";
                var json = await _redisDb.StringGetAsync(key);
                if (json.IsNullOrEmpty)
                {
                    return NotFound("Question not found.");
                }

                var questionAnswer = JsonConvert.DeserializeObject<QuestionClass>(json);
                var isAnswerCorrect = questionAnswer.Answer.Equals(request.Answer, StringComparison.OrdinalIgnoreCase);

                return Ok(isAnswerCorrect);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Internal server error");
            }
        }

    }
}
