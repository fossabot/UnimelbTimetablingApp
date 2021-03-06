﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Timetabling;
using Microsoft.AspNetCore.Hosting;
using TimetablingApp.Models;
using System.IO.Compression;
using Newtonsoft.Json.Serialization;
using System.Diagnostics;

namespace TimetablingApp.Controllers
{
    public class HomeController : Controller
    {
        private IHostingEnvironment _hostingEnvironment;

        //The list of our subjects with some basic caching
        private static List<Subject> _subjectList = null;
        private List<Subject> subjectList
        {
            get
            {
                if (_subjectList == null)
                    _subjectList = getSubjects();

                return _subjectList;
            }
        }

        public HomeController(IHostingEnvironment environment)
        {
            _hostingEnvironment = environment;
        }

        public IActionResult Index()
        {
            return View(subjectList);
        }

        //Given a list of subject codes and sorting options, generates a list of possible timetables
        [HttpPost("/Home/BuildTimetable")]
        public async Task<IActionResult> BuildTimetable([FromBody] TimetableOptionsModel model)
        {
            if (model.SubjectCodes.Count > 4)
                return StatusCode(403);

            if (System.IO.File.Exists(getModelFilePath(model)))
                return File(loadResult(model), "application/json; charset=utf-8");

            Stopwatch responseStopwatch = new Stopwatch();
            responseStopwatch.Start();

            List<Subject> subjects = await subjectsFromSubjectCodes(model.SubjectCodes);

            IEnumerable<ClassInfo> classInfos = subjects.SelectMany(subject => subject.ClassInfos);
            IEnumerable<ClassInfo> originalClassInfos = subjects.SelectMany(subject => subject.OriginalClassInfos);

            //Assign unique ids to each of the 'original' class infos, allowing for compression to work later
            int id = 0;
            foreach (var classInfo in originalClassInfos)
                classInfo.ID = id++;

            //Create a new generator with our sorting options and cancellation token
            Generator g = new Generator
            {
                SortLaterStarts = model.LaterStarts,
                SortLessDays = model.LessDays,
                CancellationToken = HttpContext.RequestAborted
            };

            long possiblePermutations = Generator.PossiblePermutationsCount(classInfos);

            List<Timetable> timetables = new List<Timetable>();

            //Check what algorithm to use, if we have over 5M permutations use the expanding algorithm
            if (possiblePermutations > 5 * 1000 * 1000)
                timetables = g.GeneratePermutationsExpanding(classInfos);
            else
                timetables = g.GenerateTimetablesBruteForce(classInfos);

            //Take 25,000 of our timetables and compress them
            var compressedTimetables = timetables.Take(25000).Select(t => new CompressedTimetable(t)).ToList();

            var result = new TimetableBuildResultModel(compressedTimetables, originalClassInfos.ToList());
            responseStopwatch.Stop();

            //Only save results for meaningfully long requests (10secs)
            if(responseStopwatch.ElapsedMilliseconds > 10 * 1000)
                saveResult(model, result);

            return Json(result);
        }

        private Stream loadResult(TimetableOptionsModel model)
        {
            string path = getModelFilePath(model);

            FileStream fileStream = System.IO.File.Open(path, FileMode.Open);

            return new GZipStream(fileStream, CompressionMode.Decompress);
        }

        //Save our timetables to the disk so we don't have to perform the calculation again
        //Possibly dangerous if someone manages to come up with ~100,000 different subject options of significant size, but I find this unlikely
        //Otherwise this will fill up 10GB of disk and break whatever it's running on
        private void saveResult(TimetableOptionsModel model, TimetableBuildResultModel result)
        {
            string path = getModelFilePath(model);

            using (FileStream fileStream = System.IO.File.Open(path, FileMode.CreateNew))
            {
                using (GZipStream compressedStream = new GZipStream(fileStream, CompressionLevel.Optimal))
                {
                    using (StreamWriter writer = new StreamWriter(compressedStream))
                    {
                        JsonSerializerSettings jsonSettings = new JsonSerializerSettings
                        {
                            ContractResolver = new CamelCasePropertyNamesContractResolver()
                        };

                        writer.Write(JsonConvert.SerializeObject(result, jsonSettings));
                    }
                }
            }
        }


        private string getModelFilePath(TimetableOptionsModel model)
        {
            string fileName = string.Join("-", model.SubjectCodes);

            fileName += "_";

            fileName += model.LaterStarts ? "1" : "0";
            fileName += model.LessDays ? "1" : "0";

            return Path.Combine(_hostingEnvironment.ContentRootPath, "TimetableCache", fileName);
        }

        //Returns the number of possible permutations from a given list of subject codes,
        [HttpPost("/Home/UpdateSelectedSubjects")]
        public async Task<IActionResult> UpdateSelectedSubjects([FromBody]TimetableOptionsModel model)
        {
            if (model.SubjectCodes.Count > 4)
                return StatusCode(403);

            List<Subject> subjects = await subjectsFromSubjectCodes(model.SubjectCodes);

            //No subjects? No possible timetables.
            if (!subjects.Any())
                return Json(0);

            IEnumerable<ClassInfo> classInfos = subjects.SelectMany(subject => subject.ClassInfos);

            //Calculate the number of possible permutations
            long numPermutations = Generator.PossiblePermutationsCount(classInfos);

            return Json(numPermutations);
        }

        private async Task<List<Subject>> subjectsFromSubjectCodes(List<string> subjectCodes)
        {
            //Find what subjects are selected from the given subject codes
            List<Subject> subjects = new List<Subject>();
            foreach (var subjectCode in subjectCodes)
            {
                Subject subject = subjectList.FirstOrDefault(s => s.Code == subjectCode);

                //If the subject doesn't exist, continue
                if (subject == null)
                    continue;

                //If we haven't loaded in the subject's timetable yet, do so
                if (subject.ClassInfos == null)
                    await subject.UpdateTimetable();

                subjects.Add(subject);
            }

            return subjects;
        }

        //Fetches the subjects from disk and creates a list from them
        private List<Subject> getSubjects()
        {
            List<Subject> subjectList = new List<Subject>();

            //Read in the list of subject codes and names from disk
            string subjectsText = System.IO.File.ReadAllText(Path.Combine(_hostingEnvironment.WebRootPath, "codes_2018_sem2.json"));
            var rawSubjects = JsonConvert.DeserializeObject<List<string>>(subjectsText);

            //Create the list of subjects from the subject codes and names
            foreach (var rawSubject in rawSubjects)
            {
                var parts = rawSubject.Split(' ');

                Subject subject = new Subject
                {
                    Code = parts[0],
                    Name = rawSubject.Substring(parts[0].Length + 1)
                };

                //Don't add duplicate subjects
                if (subjectList.All(s => s.Code != subject.Code))
                    subjectList.Add(subject);
            }

            return subjectList;
        }
    }
}