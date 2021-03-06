import { Component, OnInit, Injector } from '@angular/core';
import { AppComponentBase } from '@shared/app-component-base';
import { QuestionDto, QuestionServiceProxy } from 'shared/service-proxies/service-proxies';
import { ActivatedRoute, Router } from '@angular/router';
import { appModuleAnimation } from 'shared/animations/routerTransition';

@Component({
    selector: 'app-question-details',
    templateUrl: './question-details.component.html',
    styleUrls: ['./question-details.component.css'],
    animations: [appModuleAnimation()]
})
export class QuestionDetailsComponent extends AppComponentBase implements OnInit {
    mindfightId: number;
    tourId: number;
    questionId: number;
    question: QuestionDto;
    private routeSubscriber: any;

    constructor(
        injector: Injector,
        private questionService: QuestionServiceProxy,
        private route: ActivatedRoute,
        private router: Router
    ) {
        super(injector);
    }

    ngOnInit() {
        this.routeSubscriber = this.route.params.subscribe(params => {
            this.mindfightId = +params['mindfightId'];
            this.tourId = +params['tourId'];
            this.questionId = +params['questionId'];
        });
        this.getQuestion(this.questionId);
    }

    getQuestion(questionId): void {
        this.questionService.getQuestion(questionId).subscribe(
            (result) => {
                this.question = result;
            }
        );
    }

    ngOnDestroy() {
        this.routeSubscriber.unsubscribe();
    }
}
