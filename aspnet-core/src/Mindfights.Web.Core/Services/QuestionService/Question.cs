﻿using Abp.AspNetCore.Mvc.Authorization;
using Abp.Authorization;
using Abp.AutoMapper;
using Abp.Domain.Repositories;
using Abp.Timing;
using Abp.UI;
using Microsoft.EntityFrameworkCore;
using Mindfights.Authorization.Users;
using Mindfights.DTOs;
using Mindfights.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Abp.ObjectMapping;

namespace Mindfights.Services.QuestionService
{
    [AbpMvcAuthorize]
    public class Question : IQuestionService
    {
        private readonly IRepository<Models.Question, long> _questionRepository;
        private readonly IRepository<Team, long> _teamRepository;
        private readonly IRepository<Tour, long> _tourRepository;
        private readonly IRepository<Mindfight, long> _mindfightRepository;
        private readonly IPermissionChecker _permissionChecker;
        private readonly UserManager _userManager;
        private readonly IObjectMapper _objectMapper;
        private const int BufferSeconds = 2;

        public Question(
            IRepository<Models.Question, long> questionRepository, 
            IRepository<Team, long> teamRepository,
            IRepository<Tour, long> tourRepository,
            IRepository<Mindfight, long> mindfightRepository,
            IPermissionChecker permissionChecker,
            UserManager userManager,
            IObjectMapper objectMapper
            )
        {
            _questionRepository = questionRepository;
            _teamRepository = teamRepository;
            _tourRepository = tourRepository;
            _mindfightRepository = mindfightRepository;
            _permissionChecker = permissionChecker;
            _userManager = userManager;
            _objectMapper = objectMapper;
        }

        public async Task<List<QuestionDto>> GetAllTourQuestions(long tourId)
        {
            var currentTour = await _tourRepository
                .GetAll()
                .Include(x => x.Mindfight)
                .ThenInclude(x => x.Evaluators)
                .FirstOrDefaultAsync(x => x.Id == tourId);

            if (currentTour == null)
            {
                throw new UserFriendlyException("Turas su nurodytu id neegzistuoja!");
            }

            if (!(currentTour.Mindfight.CreatorId == _userManager.AbpSession.UserId
                  || _permissionChecker.IsGranted("ManageMindfights")
                  || currentTour.Mindfight.Evaluators.Any(x => x.UserId == _userManager.AbpSession.UserId)))
            {
                throw new AbpAuthorizationException("Jūs neturite teisės peržiūrėti šio klausimo!");
            }

            var questionsDto = new List<QuestionDto>();
            var questions = await _questionRepository
                .GetAll()
                .Where(x => x.TourId == currentTour.Id)
                .ToListAsync();

            foreach (var question in questions)
            {
                var questionDto = new QuestionDto();
                _objectMapper.Map(question, questionDto);
                questionsDto.Add(questionDto);
            }
            if (questionsDto.Count > 0)
            {
                questionsDto[questionsDto.Count - 1].IsLastQuestion = true;
            }
            return questionsDto;
        }

        public async Task<QuestionDto> GetQuestion(long questionId)
        {
            var currentQuestion = await _questionRepository
                .GetAll()
                .Include(x => x.Tour)
                .ThenInclude(x => x.Mindfight)
                .ThenInclude(x => x.Evaluators)
                .FirstOrDefaultAsync(x => x.Id == questionId);

            if (currentQuestion == null)
            {
                throw new UserFriendlyException("Klausimas su nurodytu id neegzistuoja!");
            }

            if (!(currentQuestion.Tour.Mindfight.CreatorId == _userManager.AbpSession.UserId
                  || _permissionChecker.IsGranted("ManageMindfights")
                  || currentQuestion.Tour.Mindfight.Evaluators.Any(x => x.UserId == _userManager.AbpSession.UserId)))
            {
                throw new AbpAuthorizationException("Jūs neturite teisės peržiūrėti šio klausimo!");
            }

            var question = new QuestionDto();
            _objectMapper.Map(currentQuestion, question);
            return question;
        }

        public async Task<QuestionDto> GetNextQuestion(long mindfightId, long teamId)
        {
            var currentMindfight = await _mindfightRepository
                .GetAllIncluding(mindfight => mindfight.MindfightStates)
                .FirstOrDefaultAsync(mindfight => mindfight.Id == mindfightId);

            if (currentMindfight == null)
            {
                throw new UserFriendlyException("Protmūšis su nurodytu id neegzistuoja!");
            }

            var currentTeam = await _teamRepository
                .FirstOrDefaultAsync(team => team.Id == teamId);

            if (currentTeam == null)
            {
                throw new UserFriendlyException("Komanda su nurodytu id neegzistuoja!");
            }

            var teamMindfightState = currentMindfight.MindfightStates
                .FirstOrDefault(state => state.MindfightId == mindfightId && state.TeamId == teamId);

            if (teamMindfightState == null)
            {
                throw new UserFriendlyException("Komanda pirmiausia turi pradėti turą!");
            }

            var currentTour = await _tourRepository
                .FirstOrDefaultAsync(tour => tour.Id == teamMindfightState.CurrentTourId);
            if (currentTour == null)
            {
                throw new UserFriendlyException("Problema gaunant turą iš protmūšio statuso");
            }

            Models.Question currentQuestion;
            var isFirstTourQuestion = false;
            if (teamMindfightState.CurrentQuestionId == null)
            {
                currentQuestion = await GetFirstTourQuestion(teamMindfightState.CurrentTourId);
                isFirstTourQuestion = true;
            }
            else
            {
                currentQuestion = await _questionRepository
                    .FirstOrDefaultAsync(question => question.Id == teamMindfightState.CurrentQuestionId);
            }

            if (currentQuestion == null)
            {
                throw new UserFriendlyException("Problema gaunant klausimą iš protmūšio statuso");
            }

            var nextQuestionOrderNumber = currentQuestion.OrderNumber;
            var tourIntroTime = isFirstTourQuestion ? currentTour.IntroTimeInSeconds : 0;

            if ((Clock.Now - teamMindfightState.ChangeTime).TotalSeconds >
                currentQuestion.TimeToAnswerInSeconds - BufferSeconds + tourIntroTime)
            {
                nextQuestionOrderNumber += 1;
            }

            var mindfightQuestions = await _questionRepository
                .GetAll()
                .OrderBy(x => x.OrderNumber)
                .Where(x => x.TourId == currentTour.Id && x.OrderNumber >= nextQuestionOrderNumber)
                .ToListAsync();

            if (mindfightQuestions.Count == 0)
            {
                throw new UserFriendlyException("Daugiau nėra jokių klausimų!");
            }

            var questionToReturn = mindfightQuestions.First();
            if (currentQuestion.OrderNumber != nextQuestionOrderNumber)
            {
                currentMindfight.MindfightStates.Remove(teamMindfightState);
                teamMindfightState.CurrentQuestionId = questionToReturn.Id;
                teamMindfightState.ChangeTime = Clock.Now;
                currentMindfight.MindfightStates.Add(teamMindfightState);
            }

            var questionDto = new QuestionDto();
            _objectMapper.Map(questionToReturn, questionDto);

            if (mindfightQuestions.Count == 1)
            {
                questionDto.IsLastQuestion = true;
            }

            return questionDto;
        }

        public async Task<long> CreateQuestion(QuestionDto question, long tourId)
        {
            var currentTour = await _tourRepository
                .GetAll()
                .Include(x => x.Mindfight)
                .FirstOrDefaultAsync(x => x.Id == tourId);
            if (currentTour == null)
            {
                throw new UserFriendlyException("Turas su nurodytu id neegzistuoja!");
            }

            if (!(currentTour.Mindfight.CreatorId == _userManager.AbpSession.UserId
                  || _permissionChecker.IsGranted("ManageMindfights")))
            {
                throw new AbpAuthorizationException("Jūs neturite teisės kurti klausimus!");
            }

            question.OrderNumber = await GetLastOrderNumber(tourId);
            question.OrderNumber = question.OrderNumber == 0 ? 1 : question.OrderNumber + 1;
            var points = question.Points >= 1 ? question.Points : 1;

            var questionToCreate = new Models.Question(
                currentTour,
                question.Title,
                question.Description,
                question.Answer,
                question.TimeToAnswerInSeconds,
                points,
                question.OrderNumber);
            return await _questionRepository.InsertAndGetIdAsync(questionToCreate);
        }

        public async Task UpdateQuestion(QuestionDto question, long questionId)
        {
            var questionToUpdate = await _questionRepository
                .GetAllIncluding(x => x.Tour)
                .FirstOrDefaultAsync(x => x.Id == questionId);
            if (questionToUpdate == null)
            {
                throw new UserFriendlyException("Klausimas su nurodytu id neegzistuoja!");
            }

            var currentTour = await _tourRepository
                .GetAll()
                .Include(x => x.Mindfight)
                .FirstOrDefaultAsync(x => x.Id == questionToUpdate.Tour.Id);
            if (currentTour == null)
            {
                throw new UserFriendlyException("Turas su nurodytu id neegzistuoja!");
            }

            if (!(currentTour.Mindfight.CreatorId == _userManager.AbpSession.UserId
                  || _permissionChecker.IsGranted("ManageMindfights")))
            {
                throw new AbpAuthorizationException("Jūs neturite teisės atnaujinti klausimus!");
            }
            question.OrderNumber = questionToUpdate.OrderNumber;
            _objectMapper.Map(question, questionToUpdate);
            questionToUpdate.Id = questionId;
            await _questionRepository.UpdateAsync(questionToUpdate);
        }

        public async Task DeleteQuestion(long questionId)
        {
            var questionToDelete = await _questionRepository.FirstOrDefaultAsync(x => x.Id == questionId);
            if (questionToDelete == null)
                throw new UserFriendlyException("Klausimas su nurodytu id neegzistuoja!");

            var currentTour = await _tourRepository
                .GetAll()
                .Include(x => x.Mindfight)
                .FirstOrDefaultAsync(x => x.Id == questionToDelete.TourId);

            if (currentTour == null)
                throw new UserFriendlyException("Klaida gaunant klausimo turą!");
            
            if (!(currentTour.Mindfight.CreatorId == _userManager.AbpSession.UserId
                || _permissionChecker.IsGranted("ManageMindfights")))
                throw new AbpAuthorizationException("Jūs neturite teisės trinti klausimus!");
            
            var orderNumber = questionToDelete.OrderNumber;
            await UpdateOrderNumbers(orderNumber, questionToDelete.Id, currentTour.Id);
            await _questionRepository.DeleteAsync(questionToDelete);
        }

        public async Task UpdateOrderNumber(long questionId, int newOrderNumber)
        {
            var currentQuestion = await _questionRepository.FirstOrDefaultAsync(x => x.Id == questionId);
            if (currentQuestion == null)
                throw new UserFriendlyException("Klausimas su nurodytu id neegzistuoja!");

            var currentTour = await _tourRepository
                .GetAllIncluding(x => x.Mindfight)
                .FirstOrDefaultAsync(x => x.Questions.Any(y => y.Id == questionId));
            if (currentTour == null)
                throw new UserFriendlyException("Protmūšis su nurodytu id neegzistuoja!");

            if (!(currentTour.Mindfight.CreatorId == _userManager.AbpSession.UserId
                || _permissionChecker.IsGranted("ManageMindfights")))
                throw new AbpAuthorizationException("Jūs neturite teisės keisti eilės numerius!");

            var questionWithNewOrderNumber = await _questionRepository
                .GetAll()
                .Where(x => x.TourId == currentQuestion.TourId && x.OrderNumber == newOrderNumber)
                .FirstOrDefaultAsync();

            if (questionWithNewOrderNumber == null)
            {
                var lastOrderNumber = await GetLastOrderNumber(currentQuestion.TourId);
                var lastQuestion = await _questionRepository.GetAll()
                    .Where(x => x.TourId == currentQuestion.TourId && x.OrderNumber == lastOrderNumber)
                    .FirstOrDefaultAsync();
                if (lastQuestion != null)
                {
                    lastQuestion.OrderNumber = currentQuestion.OrderNumber;
                }
                currentQuestion.OrderNumber = lastOrderNumber;
            }
            else
            {
                questionWithNewOrderNumber.OrderNumber = currentQuestion.OrderNumber;
                currentQuestion.OrderNumber = newOrderNumber;
            }
        }

        private async Task<Models.Question> GetFirstTourQuestion(long tourId)
        {
            var questionTours = await _questionRepository
                .GetAll()
                .OrderBy(x => x.OrderNumber)
                .Where(question => question.TourId == tourId)
                .ToListAsync();
            if (questionTours.Count < 1)
            {
                throw new UserFriendlyException("Turas neturi jokių klausimų!");
            }
            return questionTours.First();
        }

        private async Task UpdateOrderNumbers(int deletedOrderNumber, long deletedQuestionId, long tourId)
        {
            var questions = await _questionRepository.GetAll()
                .Where(x => x.TourId == tourId && x.Id != deletedQuestionId)
                .OrderBy(x => x.OrderNumber)
                .ToListAsync();
            var nextOrderNumber = deletedOrderNumber;
            foreach (var question in questions)
            {
                if (question.OrderNumber < deletedOrderNumber) continue;
                question.OrderNumber = nextOrderNumber;
                nextOrderNumber++;
            }
        }

        private async Task<int> GetLastOrderNumber(long tourId)
        {
            var lastOrderNumber = 0;
            var lastQuestion = await _questionRepository
                .GetAll()
                .Where(x => x.TourId == tourId)
                .OrderByDescending(x => x.OrderNumber)
                .FirstOrDefaultAsync();

            if (lastQuestion != null)
            {
                lastOrderNumber = lastQuestion.OrderNumber;
            }

            return lastOrderNumber;
        }
    }
}
